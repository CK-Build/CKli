using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Threading;

namespace CKli.Core;

/// <summary>
/// The name of this class is misleading (but easier to understand and remember): what is really cached is a default,
/// application-wide, <see cref="SocketsHttpHandler"/> (actually 2 of them: see <see cref="DefaultHandlerWithoutServerCertificateValidation"/>).
/// <para>
/// This implementation applies the recommended pattern to manage <see cref="HttpClient"/> outside of a DI world
/// (see <see href="https://learn.microsoft.com/fr-fr/dotnet/fundamentals/runtime-libraries/system-net-http-httpclient"/>).
/// But instead of a caching a HttpClient, the actual singleton is a non disposable <see cref="SocketsHttpHandler"/> hidden
/// behind the <see cref="DefaultHandler"/> static property. This enables as many <see cref="HttpClient"/> (short or long-lived)
/// to be instantiated and used: the full easy to use API is available (like <see cref="HttpClient.Timeout"/>
/// or <see cref="HttpClient.DefaultRequestHeaders"/>) without performance impacts.
/// </para>
/// <para>
/// More advanced scenarii with chain of responsibility can be supported by plumbing a chain of <see cref="DelegatingHandler"/>
/// ending with this <see cref="DefaultHandler"/>.
/// </para>
/// <para>
/// The <see cref="DefaultHandler"/> (and the one that skips server certificates that are the ultimate <see cref="SocketsHttpHandler"/>)
/// can be configured but only once, before any attempt to use them.
/// <see cref="Initialize"/> return false if the default handler and the <see cref="DefaultHandlerWithoutServerCertificateValidation"/>
/// were already initialized.
/// </para>
/// </summary>
/// <remarks>
/// This uses the <see cref="SocketsHttpHandler"/> directly instead of the <see cref="HttpClientHandler"/> that is a thin layer
/// around it that doesn't bring relevant functionalities. The default configuration is kept as-is. For instance:
/// <list type="bullet">
///     <item>
///     The <see cref="SocketsHttpHandler.PooledConnectionIdleTimeout"/> is infinite: this won't work well in an environment where
///     DNS records change.
///     </item>
///     <item>
///     The <see cref="SocketsHttpHandler.UseCookies"/> is true: this may not be a problem as this is used to call API endpoints that never use
///     cookies but cookies are handled with the limitations of the <see cref="CookieContainer"/>.
///     </item>
/// </list>
/// All these defaults can be altered by using <see cref="Initialize"/> at the start of the application.
/// </remarks>
public static class SharedHttpClient
{
    /// <summary>
    /// The type of shared client.
    /// </summary>
    public enum SharedHandlerType
    {
        /// <summary>
        /// The <see cref="DefaultHandler"/> that expects remote servers certificate to be
        /// verifiable and valid.
        /// </summary>
        Regular,

        /// <summary>
        /// The <see cref="DefaultHandlerWithoutServerCertificateValidation"/> that fully ignores
        /// the remote certificates.
        /// </summary>
        WithoutServerCertificateValidation
    }

    /// <summary>
    /// Creates a new HttpClient bound to the <see cref="DefaultHandler"/>.
    /// </summary>
    /// <returns>A new HttpClient.</returns>
    public static HttpClient Create()
    {
        // Using disposeHandler: false here is not required as the shell is here to
        // cut the Dispose propagation. This simply avoids the useless call :-).
        return new HttpClient( HandlerShell.Instance, disposeHandler: false );
    }

    /// <summary>
    /// Gets the default handler to use: <see cref="DelegatingHandler"/> can be built on it to
    /// initialize a HttpClient with more behavior than the default one.
    /// </summary>
    public static HttpMessageHandler DefaultHandler => HandlerShell.Instance;

    /// <summary>
    /// Gets a default handler that can be used when remote server certificates are invalid or cannot
    /// be verified. This is a dangerous option: the identity of the remote servers are not guaranteed
    /// (by their exposed certificates) but, sometimes, it is the only way to make things work...
    /// </summary>
    /// <remarks>
    /// The <see cref="Initialize(Action{SharedHandlerType, SocketsHttpHandler})"/> can be used to secure this:
    /// this handler may also be "safe", if needed, to secure the running application.
    /// </remarks>
    public static HttpMessageHandler DefaultHandlerWithoutServerCertificateValidation => HandlerShell.Instance2;

    /// <summary>
    /// Initializes the <see cref="DefaultHandler"/> (and the <see cref="DefaultHandlerWithoutServerCertificateValidation"/>)
    /// if not already initialized.
    /// </summary>
    /// <param name="configuration">
    /// The configuration to apply.
    /// This configuration can be called twice: first for the <see cref="DefaultHandler"/> and then, if the
    /// initialization has been done, for the <see cref="DefaultHandlerWithoutServerCertificateValidation"/>.
    /// The <see cref="SharedHandlerType"/> may allow 2 different configurations.
    /// <para>
    /// When called with the <see cref="SharedHandlerType.WithoutServerCertificateValidation"/>, the provided
    /// new handler has its <see cref="SocketsHttpHandler.SslOptions"/>'s <see cref="SslClientAuthenticationOptions.RemoteCertificateValidationCallback"/>
    /// sets to a dumb callback that always returns true. The configuration function is free to change this: by resetting
    /// this back to null (or to the same behavior as the default handler), the configuration can prevent any participant
    /// to skip remote certificates validation.
    /// </para>
    /// </param>
    /// <returns>True if the handler has been initialized by this call, false if it was already configure.</returns>
    public static bool Initialize( Action<SharedHandlerType,SocketsHttpHandler> configuration )
    {
        HandlerShell.Initialize( configuration, out var hasBeenInitialized );
        return hasBeenInitialized;
    }

    /// <summary>
    /// Isolates the application-wide singleton <see cref="HttpClientHandler"/>.
    /// This must be a <see cref="DelegatingHandler"/> because it is the only <see cref="HttpMessageHandler"/>
    /// that can call the <see cref="HttpMessageHandler.SendAsync"/> (and Send) methods that are "internal protected"
    /// but we expose it as a HttpMessageHandler.
    /// </summary>
    sealed class HandlerShell : DelegatingHandler
    {
        static HandlerShell? _instance;
        static HandlerShell? _instance2;

        HandlerShell( SocketsHttpHandler actual )
            : base( actual )
        {
        }

        internal static HandlerShell Instance => _instance ?? Initialize( null, out _ );

        internal static HandlerShell Instance2
        {
            get
            {
                if( _instance2 == null )
                {
                    Initialize( null, out _ );
                }
                return _instance2;
            }
        }

        [MemberNotNull( nameof( _instance ), nameof( _instance2 ) )]
        internal static HandlerShell Initialize( Action<SharedHandlerType, SocketsHttpHandler>? configuration,
                                                 out bool hasBeenInitialized )
        {
            var i = _instance;
            hasBeenInitialized = i == null;
            if( hasBeenInitialized )
            {
                var h = new SocketsHttpHandler();
                configuration?.Invoke( SharedHandlerType.Regular, h );
                var newOne = new HandlerShell( h );
                i = Interlocked.CompareExchange( ref _instance, newOne, null );
                if( i == null )
                {
                    i = newOne;
                    var h2 = new SocketsHttpHandler();
                    h2.SslOptions.RemoteCertificateValidationCallback = static ( message, cert, chain, errors ) => true;
                    configuration?.Invoke( SharedHandlerType.WithoutServerCertificateValidation, h2 );
                    var newOne2 = new HandlerShell( h2 );
                    Interlocked.Exchange( ref _instance2, newOne2 );
                }
                else
                {
                    h.Dispose();
                    hasBeenInitialized = false;
                }
            }
            Throw.DebugAssert( _instance != null && _instance2 != null && i == _instance );
            return i;
        }

        protected override void Dispose( bool disposing )
        {
            // Breaks the dispose chain.
        }
    }

}

