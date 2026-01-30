using System;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Base class for <see cref="HttpGitHostingProvider"/> implemented in a plugin.
/// <para>
/// Instances are automatically tracked and released when unloading plugins.
/// </para>
/// </summary>
public abstract class PluginHttpGitHostingProvider : HttpGitHostingProvider, IInternalPluginGitHostingProvider
{
    IInternalPluginGitHostingProvider? _next;
    IInternalPluginGitHostingProvider? _prev;
    bool _disposed;

    /// <summary>
    /// Initializes a new provider.
    /// </summary>
    /// <param name="baseUrl">The <see cref="GitHostingProvider.BaseUrl"/>.</param>
    /// <param name="gitKey">The key that identifies this provider and to use for PATs.</param>
    /// <param name="baseApiUrl">The api endpoint to call.</param>
    protected PluginHttpGitHostingProvider( string baseUrl, IGitRepositoryAccessKey gitKey, Uri baseApiUrl )
        : base( baseUrl, KnownCloudGitProvider.Unknown, gitKey, baseApiUrl )
    {
        GitHostingProvider.Register( this, out _next, out _prev );
    }

    IInternalPluginGitHostingProvider? IInternalPluginGitHostingProvider.Next
    {
        get => _next;
        set => _next = value;
    }

    IInternalPluginGitHostingProvider? IInternalPluginGitHostingProvider.Prev
    {
        get => _prev;
        set => _prev = value;
    }

    /// <summary>
    /// Unregisters this provider and calls <see cref="DoDispose()"/> (only once).
    /// </summary>
    public void Dispose()
    {
        if( _disposed ) return;
        _disposed = true;
        GitHostingProvider.Unregister( this );
        _next = null;
        _prev = null;
        DoDispose();
    }

    /// <summary>
    /// Must release any managed resources.
    /// <para>
    /// We don't support the <see href="https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/dispose-pattern">"Dispose Pattern"</see>
    /// and this is deliberate: unmanaged resources should not be used by this kind of services.
    /// </para>
    /// </summary>
    protected abstract void DoDispose();

}
