using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core.GitHosting.Providers;

/// <summary>
/// Helper base class for remote providers via http that provides a <see cref="HttpClient"/>
/// and an optional <see cref="OnSendHookAsync"/> extension point.
/// </summary>
public abstract partial class HttpGitHostingProvider : GitHostingProvider
{
    readonly HttpMessageHandler _handler;
    readonly Uri _baseApiUrl;
    readonly bool _alwaysUseAuthentication;

    /// <summary>
    /// Initializes a base provider.
    /// </summary>
    /// <param name="baseUrl">The <see cref="GitHostingProvider.BaseUrl"/>.</param>
    /// <param name="gitKey">The key that identifies this provider and provides the authorizations.</param>
    /// <param name="baseApiUrl">The <see cref="HttpClient.BaseAddress"/> to use.</param>
    /// <param name="alwaysUseAuthentication">
    /// On a public repository but we may not want to use public, unauthenticated, API access (because the API
    /// requires authentication or because of rate limits).
    /// <para>
    /// Setting this to true makes <see cref="EnsureReadAccess"/> use the <see cref="IGitRepositoryAccessKey.ToPrivateAccessKey()"/>.
    /// </para>
    /// </param>
    /// <param name="skipRemoteServerCertificateValidation">True to not validate server certificates.</param>
    private protected HttpGitHostingProvider( string baseUrl,
                                              IGitRepositoryAccessKey gitKey,
                                              Uri baseApiUrl,
                                              bool alwaysUseAuthentication,
                                              bool skipRemoteServerCertificateValidation = false )
        : base( baseUrl, gitKey )
    {
        _handler = skipRemoteServerCertificateValidation
                    ? SharedHttpClient.DefaultHandlerWithoutServerCertificateValidation
                    : SharedHttpClient.DefaultHandler;
        _baseApiUrl = baseApiUrl;
        _alwaysUseAuthentication = alwaysUseAuthentication;
    }

    /// <summary>
    /// Gets the base API url.
    /// </summary>
    protected Uri BaseApiUrl => _baseApiUrl;

    /// <inheritdoc />
    public sealed override async Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                                     NormalizedPath repoPath,
                                                                                     CancellationToken cancellation = default )
    {
        if( !EnsureReadAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return null;
        }
        try
        {
            return await GetRepositoryInfoAsync( monitor, client, repoPath, cancellation ).ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return null;
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <inheritdoc cref="GitHostingProvider.GetRepositoryInfoAsync(IActivityMonitor, NormalizedPath, CancellationToken)"/>
    /// <param name="client">The HttpClient to use.</param>
    protected abstract Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                           HttpClient client,
                                                                           NormalizedPath repoPath,
                                                                           CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                                    NormalizedPath repoPath,
                                                                                    HostedRepositoryCreateOptions? options = null,
                                                                                    CancellationToken cancellation = default )
    {
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return null;
        }
        try
        {
            return await CreateRepositoryAsync( monitor, client, repoPath, options, cancellation ).ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return null;
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <inheritdoc cref="GitHostingProvider.CreateRepositoryAsync(IActivityMonitor, NormalizedPath, HostedRepositoryCreateOptions?, CancellationToken)"/>
    /// <param name="client">The HttpClient to use.</param>
    protected abstract Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                          HttpClient client,
                                                                          NormalizedPath repoPath,
                                                                          HostedRepositoryCreateOptions? options,
                                                                          CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor, NormalizedPath repoPath, CancellationToken cancellation = default )
    {
        Throw.CheckState( CanArchiveRepository );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return false;
        }
        try
        {
            return await ArchiveRepositoryAsync( monitor, client, repoPath, cancellation ).ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return false;
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <inheritdoc cref="GitHostingProvider.ArchiveRepositoryAsync(IActivityMonitor, NormalizedPath, CancellationToken)"/>
    /// <param name="client">The HttpClient to use.</param>
    protected abstract Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                          HttpClient client,
                                                          NormalizedPath repoPath,
                                                          CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                                   NormalizedPath repoPath,
                                                                   CancellationToken cancellation = default )
    {
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return false;
        }
        try
        {
            return await DeleteRepositoryAsync( monitor, client, repoPath, cancellation ).ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return false;
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <inheritdoc cref="GitHostingProvider.DeleteRepositoryAsync(IActivityMonitor, NormalizedPath, CancellationToken)"/>
    /// <param name="client">The HttpClient to use.</param>
    protected abstract Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                         HttpClient client,
                                                         NormalizedPath repoPath,
                                                         CancellationToken cancellation );

    bool EnsureReadAccess( IActivityMonitor monitor,
                           ref NormalizedPath repoPath,
                           [NotNullWhen(true)]out HttpClient? httpClient,
                           CancellationToken userCancellation )
    {
        httpClient = null;
        repoPath = ValidateRepoPath( monitor, repoPath );
        if( repoPath.IsEmptyPath ) return false;
        // Regular PAT resolution.
        if( !GitKey.GetReadCredentials( monitor, out var creds ) )
        {
            return false;
        }
        // Success but null secret: we necessarily are on a public repository but we may not
        // want to use public, unauthenticated, API access (because the API requires authentication
        // even on a public repository or because of rate limits).
        // TODO: Add GetOptionalReadCredentials that doesn't trigger error nor warning.
        Throw.DebugAssert( creds != null || GitKey.IsPublic );
        if( creds == null
            && _alwaysUseAuthentication
            && !GitKey.ToPrivateAccessKey().GetReadCredentials( monitor, out creds ) )
        {
            return false;
        }
        CreateClient( monitor, creds?.Password, out httpClient, userCancellation );
        return true;
    }

    bool EnsureWriteAccess( IActivityMonitor monitor,
                            ref NormalizedPath repoPath,
                            [NotNullWhen( true )] out HttpClient? httpClient,
                           CancellationToken userCancellation )
    {
        httpClient = null;
        repoPath = ValidateRepoPath( monitor, repoPath );
        if( repoPath.IsEmptyPath ) return false;
        if( !GitKey.GetWriteCredentials( monitor, out var creds ) )
        {
            return false;
        }
        CreateClient( monitor, creds.Password, out httpClient, userCancellation );
        return true;
    }

    void CreateClient( IActivityMonitor monitor,
                       string? secret,
                       out HttpClient httpClient,
                       CancellationToken userCancellation )
    {
        Hook h = new Hook( this, monitor, userCancellation );
        httpClient = new HttpClient( h );
        h._httpClient = httpClient;
        DefaultConfigure( httpClient );
        SetAuthorizationHeader( httpClient.DefaultRequestHeaders, secret );
    }

    /// <summary>
    /// Centralized validation (and potentially automatic adaptation) of a repository path:
    /// returning <c>default</c> (that is invalid) should also log an error explaining the why.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repoPath">The repository path to work with.</param>
    /// <returns>The actual repository path to consider or an empty path in case of error.</returns>
    protected abstract NormalizedPath ValidateRepoPath( IActivityMonitor monitor, NormalizedPath repoPath );

    /// <summary>
    /// Extension point to configure the <see cref="HttpClient"/>.
    /// By default, <see cref="HttpClient.BaseAddress"/> is set to <see cref="BaseApiUrl"/> and
    /// a User-Agent header is set to 'CKli-GitHosting/1.0'.
    /// </summary>
    /// <param name="client">The client to configure.</param>
    protected virtual void DefaultConfigure( HttpClient client )
    {
        client.BaseAddress = _baseApiUrl;
        client.DefaultRequestHeaders.UserAgent.Add( new ProductInfoHeaderValue( "CKli-GitHosting", "1.0" ) );
    }

    /// <summary>
    /// Must configures the request headers with the appropriate authorization.
    /// By default, this adds the <c>Authorization: Bearer &lt;secret&gt;</c> or
    /// removes it if <paramref name="secret"/> is null.
    /// </summary>
    /// <param name="headers">The request headers.</param>
    /// <param name="secret">The authorization secret.</param>
    protected virtual void SetAuthorizationHeader( HttpRequestHeaders headers, string? secret )
    {
        headers.Authorization = secret != null
                                ? new AuthenticationHeaderValue( "Bearer", secret )
                                : null;
    }

    /// <summary>
    /// Hook called on requests (provides a <see cref="DelegatingHandler"/> capability).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="request">The request to send.</param>
    /// <param name="sendAsync">The actual send function that can be called more than once for retries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The server response.</returns>
    protected virtual async Task<HttpResponseMessage> OnSendHookAsync( IActivityMonitor monitor,
                                                                       HttpRequestMessage request,
                                                                       Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
                                                                       CancellationToken cancellationToken )
    {
        TimeSpan? delay;
        HttpResponseMessage response;
        retry:
        response = await sendAsync( request, cancellationToken ).ConfigureAwait( false );
        if( IsSuccessfulResponse( response ) )
        {
            return OnSuccessfulResponse( monitor, response );
        }
        delay = await OnFailedResponseAsync( monitor, response );
        if( delay != null )
        {
            await Task.Delay( delay.Value, cancellationToken ).ConfigureAwait( false );
            goto retry;
        }
        return response;
    }

    /// <summary>
    /// Gets whether this response must be considered successful.
    /// Returns <see cref="HttpResponseMessage.IsSuccessStatusCode"/> by default.
    /// </summary>
    /// <param name="response">The response message.</param>
    /// <returns>True if this is a successful response, false otherwise.</returns>
    protected virtual bool IsSuccessfulResponse( HttpResponseMessage response )
    {
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Called by default <see cref="OnSendHookAsync"/> when a successful response has been received
    /// (according to <see cref="IsSuccessfulResponse(HttpResponseMessage)"/>).
    /// Does nothing by default: returns the <paramref name="response"/> as-is and logs nothing.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="response">The successful response.</param>
    /// <returns>The response.</returns>
    protected virtual HttpResponseMessage OnSuccessfulResponse( IActivityMonitor monitor, HttpResponseMessage response )
    {
        return response;
    }

    /// <summary>
    /// Called by default <see cref="OnSendHookAsync"/> when a failed response has been received
    /// (according to <see cref="IsSuccessfulResponse(HttpResponseMessage)"/>).
    /// <para>
    /// By default calls <see cref="LogResponse"/> and doesn't retry (returns a null delay).
    /// </para>
    /// Does nothing by default: returns a null delay.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="response">The unsuccessful response.</param>
    /// <returns>The delay to wait before retrying, null to not retry an consider this response as the final one.</returns>
    protected virtual async Task<TimeSpan?> OnFailedResponseAsync( IActivityMonitor monitor, HttpResponseMessage response )
    {
        await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
        return null;
    }

    protected virtual async Task LogResponseAsync( IActivityMonitor monitor, HttpResponseMessage response, LogLevel logLevel )
    {
        string dumpResponse = await response.Content.ReadAsStringAsync().ConfigureAwait( false );
        monitor.Log( logLevel, $"""
            Request '{response.RequestMessage?.RequestUri}' received:
            {response}
            With content:
            {dumpResponse}
            """ );
    }
}
