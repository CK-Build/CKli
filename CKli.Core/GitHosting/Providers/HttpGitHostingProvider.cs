using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    /// When on a public repository, we may not want to use public, unauthenticated, API access (because the API
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
        Throw.CheckArgument( "Must be 'https://xxx.com' or 'https://xxx.com/api/'.",
                             baseApiUrl.IsAbsoluteUri && (baseApiUrl.AbsolutePath.Length == 0 || baseApiUrl.AbsolutePath[^1] == '/') );
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

    /// <summary>
    /// Gets whether a read PAT must be used for read-only operation on public repositories.
    /// <para>
    /// Unauthenticated API access should not be used (because the API always requires authentication or because of rate limits):
    /// when true, <see cref="EnsureReadAccess"/> uses the <see cref="IGitRepositoryAccessKey.ToPrivateAccessKey()"/>.
    /// </para>
    /// </summary>
    public bool AlwaysUseAuthentication => _alwaysUseAuthentication;

    /// <inheritdoc />
    public sealed override async Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                                     NormalizedPath repoPath,
                                                                                     bool mustExist,
                                                                                     CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"Reading repository information from '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureReadAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return null;
        }
        try
        {
            return await GetRepositoryInfoAsync( monitor, client, repoPath, mustExist, cancellation ).ConfigureAwait( false );
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

    /// <inheritdoc cref="GitHostingProvider.GetRepositoryInfoAsync"/>
    protected abstract Task<HostedRepositoryInfo?> GetRepositoryInfoAsync( IActivityMonitor monitor,
                                                                           HttpClient client,
                                                                           NormalizedPath repoPath,
                                                                           bool mustExist,
                                                                           CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                                    NormalizedPath repoPath,
                                                                                    bool? isPrivate = null,
                                                                                    string defaultBranchName = "main",
                                                                                    CancellationToken cancellation = default )
    {
        bool fPrivate = isPrivate ?? !IsDefaultPublic;
        using var _ = monitor.OpenInfo( $"Creating {(fPrivate ? "private" : "public")} repository '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return null;
        }
        try
        {
            return await CreateRepositoryAsync( monitor, client, repoPath, fPrivate, defaultBranchName, cancellation ).ConfigureAwait( false );
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

    /// <summary>
    /// Creates a new repository.
    /// </summary>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="client">The http client to use.</param>
    /// <param name="repoPath">The repository path in this provider.</param>
    /// <param name="isPrivate">Whether the repository must be private (or public).</param>
    /// <param name="defaultBranchName">The default branch name to initialize.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The created repository info or null on error.</returns>
    protected abstract Task<HostedRepositoryInfo?> CreateRepositoryAsync( IActivityMonitor monitor,
                                                                          HttpClient client,
                                                                          NormalizedPath repoPath,
                                                                          bool isPrivate,
                                                                          string defaultBranchName,
                                                                          CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<bool> SetDefaultBranchAsync( IActivityMonitor monitor,
                                                                   NormalizedPath repoPath,
                                                                   string branchName,
                                                                   CancellationToken cancellation = default )
    {
        Throw.CheckState( HasDefaultBranch );
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
        using var _ = monitor.OpenInfo( $"Setting default branch of repository '{repoPath}' on '{BaseUrl}' to '{branchName}'." );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return false;
        }
        try
        {
            // No read before the write here (as opposed to ArchiveRepositoryAsync): hosts accept a write that
            // sets the branch that is already the default one, and the repository representation they serve
            // right after a write can be stale, which would make a "no change needed" shortcut unreliable.
            return await SetDefaultBranchAsync( monitor, client, repoPath, branchName, cancellation ).ConfigureAwait( false );
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

    /// <inheritdoc cref="GitHostingProvider.SetDefaultBranchAsync(IActivityMonitor, NormalizedPath, string, CancellationToken)"/>
    protected abstract Task<bool> SetDefaultBranchAsync( IActivityMonitor monitor,
                                                         HttpClient client,
                                                         NormalizedPath repoPath,
                                                         string branchName,
                                                         CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                                    NormalizedPath repoPath,
                                                                    bool archive,
                                                                    CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"{(archive ? "A" : "Una")}rchiving repository '{repoPath}' on '{BaseUrl}'." );
        Throw.CheckState( CanArchiveRepository );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return false;
        }
        try
        {
            // The current state must be read before acting: hosts reject a state change that is already
            // done (GitHub answers a 422 with no error detail when patching an archived repository) and
            // archiving must be idempotent.
            var info = await GetRepositoryInfoAsync( monitor, client, repoPath, mustExist: true, cancellation ).ConfigureAwait( false );
            if( info == null ) return false;
            if( info.IsArchived == archive )
            {
                monitor.Info( $"Repository '{BaseUrl}/{repoPath}' is already {(archive ? "" : "un")}archived." );
                return true;
            }
            return await ArchiveRepositoryAsync( monitor, client, repoPath, archive, cancellation ).ConfigureAwait( false );
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

    /// <inheritdoc cref="GitHostingProvider.ArchiveRepositoryAsync(IActivityMonitor, NormalizedPath, bool, CancellationToken)"/>
    protected abstract Task<bool> ArchiveRepositoryAsync( IActivityMonitor monitor,
                                                          HttpClient client,
                                                          NormalizedPath repoPath,
                                                          bool archive,
                                                          CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                                   NormalizedPath repoPath,
                                                                   CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"Deleting repository '{repoPath}' on '{BaseUrl}'." );

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
            monitor.Error( $"Error while deleting repository '{repoPath}' on '{BaseUrl}'.", ex );
            return false;
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <inheritdoc cref="GitHostingProvider.DeleteRepositoryAsync(IActivityMonitor, NormalizedPath, CancellationToken)"/>
    protected abstract Task<bool> DeleteRepositoryAsync( IActivityMonitor monitor,
                                                         HttpClient client,
                                                         NormalizedPath repoPath,
                                                         CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<(bool Success, byte[]? Content)> GetFileContentAsync( IActivityMonitor monitor,
                                                                                            NormalizedPath repoPath,
                                                                                            NormalizedPath filePath,
                                                                                            string? refName = null,
                                                                                            LogLevel notFoundLogLevel = LogLevel.Trace,
                                                                                            CancellationToken cancellation = default )
    {
        Throw.CheckArgument( !filePath.IsEmptyPath );
        using var _ = monitor.OpenInfo( $"Reading file '{filePath}' of '{repoPath}'@'{refName ?? "(default branch)"}' on '{BaseUrl}'." );
        if( !EnsureReadAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return (false, null);
        }
        try
        {
            return await GetFileContentAsync( monitor, client, repoPath, filePath, refName, notFoundLogLevel, cancellation )
                            .ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return (false, null);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// Provider-specific file read implementation using an HttpClient.
    /// <para>
    /// <see cref="ReadFileResponseAsync"/> handles the response, including the not found case.
    /// </para>
    /// </summary>
    protected abstract Task<(bool Success, byte[]? Content)> GetFileContentAsync( IActivityMonitor monitor,
                                                                                  HttpClient client,
                                                                                  NormalizedPath repoPath,
                                                                                  NormalizedPath filePath,
                                                                                  string? refName,
                                                                                  LogLevel notFoundLogLevel,
                                                                                  CancellationToken cancellation );

    /// <summary>
    /// Handles the response of a file read: reads the bytes on success, answers <c>(true,null)</c> on a
    /// <see cref="HttpStatusCode.NotFound"/> (logging at <paramref name="notFoundLogLevel"/>) and logs the
    /// response as an error otherwise.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="response">The response to handle.</param>
    /// <param name="repoPath">The repository path (used by the not found message).</param>
    /// <param name="filePath">The file path (used by the not found message).</param>
    /// <param name="notFoundLogLevel">The log level to use when the file doesn't exist.</param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>Whether the call succeeded, and the file content if it exists.</returns>
    protected async Task<(bool Success, byte[]? Content)> ReadFileResponseAsync( IActivityMonitor monitor,
                                                                                 HttpResponseMessage response,
                                                                                 NormalizedPath repoPath,
                                                                                 NormalizedPath filePath,
                                                                                 LogLevel notFoundLogLevel,
                                                                                 CancellationToken cancellation )
    {
        if( response.StatusCode == HttpStatusCode.NotFound )
        {
            monitor.Log( notFoundLogLevel, $"File '{filePath}' not found in '{BaseUrl}/{repoPath}'." );
            return (true, null);
        }
        if( !response.IsSuccessStatusCode )
        {
            await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
            return (false, null);
        }
        var content = await response.Content.ReadAsByteArrayAsync( cancellation ).ConfigureAwait( false );
        monitor.Trace( $"Read {content.Length} byte(s)." );
        return (true, content);
    }

    /// <inheritdoc />
    public sealed override async Task<(bool Success, PublishedReleaseInfo? Info)> GetReleaseAsync( IActivityMonitor monitor,
                                                                                                   NormalizedPath repoPath,
                                                                                                   string releaseId,
                                                                                                   LogLevel notFoundLogLevel = LogLevel.Trace,
                                                                                                   CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"Reading release '{releaseId}' for '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureReadAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return (false,null);
        }
        try
        {
            return await GetReleaseAsync( monitor, client, repoPath, releaseId, notFoundLogLevel, cancellation )
                            .ConfigureAwait( false );
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return (false, null);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// Provider-specific single-release read implementation using an HttpClient.
    /// <para>
    /// <see cref="GitHostingProvider.LogReleaseNotFound"/> answers the not found case.
    /// </para>
    /// </summary>
    protected abstract Task<(bool Success, PublishedReleaseInfo? Info)> GetReleaseAsync( IActivityMonitor monitor,
                                                                                         HttpClient client,
                                                                                         NormalizedPath repoPath,
                                                                                         string releaseId,
                                                                                         LogLevel notFoundLogLevel,
                                                                                         CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<bool> DeleteReleaseAsync( IActivityMonitor monitor,
                                                                NormalizedPath repoPath,
                                                                string releaseId,
                                                                CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"Deleting release '{releaseId}' for '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return false;
        }
        try
        {
            return await DeleteReleaseAsync( monitor, client, repoPath, releaseId, cancellation ).ConfigureAwait( false );
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

    /// <summary>
    /// Provider-specific single-release deletion implementation using an HttpClient.
    /// </summary>
    protected abstract Task<bool> DeleteReleaseAsync( IActivityMonitor monitor,
                                                      HttpClient client,
                                                      NormalizedPath repoPath,
                                                      string releaseId,
                                                      CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<string?> CreateDraftReleaseAsync( IActivityMonitor monitor,
                                                                        NormalizedPath repoPath,
                                                                        string versionedTag,
                                                                        CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"Creating draft release for '{versionedTag}' in repository '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return null;
        }
        try
        {
            var releaseId = await CreateDraftReleaseAsync( monitor, client, repoPath, versionedTag, cancellation ).ConfigureAwait( false );
            monitor.CloseGroup( $"ReleaseId = {releaseId}" );
            return releaseId;
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

    /// <inheritdoc cref="GitHostingProvider.CreateDraftReleaseAsync(IActivityMonitor, NormalizedPath, string, CancellationToken)"/>
    protected abstract Task<string?> CreateDraftReleaseAsync( IActivityMonitor monitor,
                                                              HttpClient client,
                                                              NormalizedPath repoPath,
                                                              string versionedTag,
                                                              CancellationToken cancellation );

    /// <summary>
    /// Public entry point: ensure read access then call provider-specific implementation using HttpClient.
    /// </summary>
    public sealed override async Task<List<PublishedReleaseInfo>?> GetReleaseListAsync( IActivityMonitor monitor,
                                                                                        NormalizedPath repoPath,
                                                                                        int pageNumber,
                                                                                        int countPerPage,
                                                                                        CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"Reading published releases for '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureReadAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return null;
        }
        try
        {
            return await GetReleaseListAsync( monitor, client, repoPath, pageNumber, countPerPage, cancellation ).ConfigureAwait( false );
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

    /// <summary>
    /// Provider-specific implementation that calls the hosting REST API using an HttpClient.
    /// </summary>
    protected abstract Task<List<PublishedReleaseInfo>?> GetReleaseListAsync( IActivityMonitor monitor,
                                                                              HttpClient client,
                                                                              NormalizedPath repoPath,
                                                                              int pageNumber,
                                                                              int countPerPage,
                                                                              CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<bool> AddReleaseAssetAsync( IActivityMonitor monitor,
                                                                  NormalizedPath repoPath,
                                                                  string releaseIdentifier,
                                                                  NormalizedPath filePath,
                                                                  string? fileName = null,
                                                                  CancellationToken cancellation = default )
    {
        fileName ??= filePath.LastPart;
        using var _ = monitor.OpenInfo( $"Adding asset '{fileName}' in release '{releaseIdentifier}' of repository '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return false;
        }
        try
        {
            return await AddReleaseAssetAsync( monitor, client, repoPath, releaseIdentifier, filePath, fileName, cancellation ).ConfigureAwait( false );
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

    /// <inheritdoc cref="GitHostingProvider.AddReleaseAssetAsync(IActivityMonitor, NormalizedPath, string, NormalizedPath, string?, CancellationToken)"/>
    protected abstract Task<bool> AddReleaseAssetAsync( IActivityMonitor monitor,
                                                        HttpClient client,
                                                        NormalizedPath repoPath,
                                                        string releaseIdentifier,
                                                        NormalizedPath filePath,
                                                        string fileName,
                                                        CancellationToken cancellation );

    /// <inheritdoc />
    public sealed override async Task<bool> AddReleaseAssetsAsync( IActivityMonitor monitor,
                                                                   NormalizedPath repoPath,
                                                                   string releaseIdentifier,
                                                                   NormalizedPath assetsFolder,
                                                                   CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"Adding assets files from '{assetsFolder}' in release '{releaseIdentifier}' of repository '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return false;
        }
        try
        {
            return await AddReleaseAssetsAsync( monitor, client, repoPath, releaseIdentifier, assetsFolder, cancellation ).ConfigureAwait( false );
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

    /// <inheritdoc cref="GitHostingProvider.AddReleaseAssetsAsync(IActivityMonitor, NormalizedPath, string, NormalizedPath, CancellationToken)"/>
    protected virtual async Task<bool> AddReleaseAssetsAsync( IActivityMonitor monitor,
                                                             HttpClient client,
                                                             NormalizedPath repoPath,
                                                             string releaseIdentifier,
                                                             NormalizedPath assetsFolder,
                                                             CancellationToken cancellation )
    {
        foreach( var f in Directory.GetFiles( assetsFolder ) )
        {
            NormalizedPath filePath = f;
            if( !await AddReleaseAssetAsync( monitor, repoPath, releaseIdentifier, filePath, filePath.LastPart, cancellation ).ConfigureAwait( false ) )
            {
                return false; 
            }
        }
        return true;
    }

    /// <inheritdoc />
    public sealed override async Task<bool> FinalizeReleaseAsync( IActivityMonitor monitor,
                                                                  NormalizedPath repoPath,
                                                                  string releaseIdentifier,
                                                                  CancellationToken cancellation = default )
    {
        using var _ = monitor.OpenInfo( $"Finalizing draft release '{releaseIdentifier}' of repository '{repoPath}' on '{BaseUrl}'." );
        if( !EnsureWriteAccess( monitor, ref repoPath, out var client, cancellation ) )
        {
            return false;
        }
        try
        {
            return await FinalizeReleaseAsync( monitor, client, repoPath, releaseIdentifier, cancellation ).ConfigureAwait( false );
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

    /// <inheritdoc cref="GitHostingProvider.FinalizeReleaseAsync(IActivityMonitor, NormalizedPath, string, CancellationToken)"/>
    protected abstract Task<bool> FinalizeReleaseAsync( IActivityMonitor monitor, HttpClient client, NormalizedPath repoPath, string releaseIdentifier, CancellationToken cancellation );


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
        Throw.DebugAssert( creds != null || IsDefaultPublic );
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
    /// Logs a standard "Repository not found" error and always returns null.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repoPath">The not found repository.</param>
    /// <returns>Always null.</returns>
    protected HostedRepositoryInfo? LogErrorNotFound( IActivityMonitor monitor, NormalizedPath repoPath )
    {
        monitor.Error( $"Expected Git repository at '{BaseUrl}/{repoPath}' is missing." );
        return null;
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
        OnStartRequest( monitor, request );
        HttpResponseMessage response;
        retry:
        response = await sendAsync( request, cancellationToken ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            var delay = await GetRetryDelayAsync( monitor, request, response ).ConfigureAwait( false );
            if( delay != null )
            {
                await Task.Delay( delay.Value, cancellationToken ).ConfigureAwait( false );
                goto retry;
            }
        }
        return response;
    }

    /// <summary>
    /// Extension point called by <see cref="OnSendHookAsync"/>. Does nothing by default.
    /// <para>
    /// Per request state must be registered in <see cref="HttpRequestMessage.Options"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="request">The starting request.</param>
    protected virtual void OnStartRequest( IActivityMonitor monitor, HttpRequestMessage request )
    {
    }

    /// <summary>
    /// Called by <see cref="OnSendHookAsync"/> for a response that is not a success status code, to know
    /// whether the request must be sent again. Returns null by default: no retry.
    /// <para>
    /// This is only about retrying and it logs nothing. What a status code means is known by the operation
    /// that issued the request, and only it can tell a refusal from an expected answer: a 404 is an error
    /// for a release creation and the plain answer of a "does this repository exist?". Concrete providers
    /// test the <see cref="HttpResponseMessage.StatusCode"/> and report what they don't expect with
    /// <see cref="LogFailedAsync(IActivityMonitor, HttpResponseMessage)"/>.
    /// </para>
    /// <para>
    /// Per request state (like a <see cref="HttpRetryState"/> instance) should have been registered
    /// in <see cref="HttpRequestMessage.Options"/> by <see cref="OnStartRequest(IActivityMonitor, HttpRequestMessage)"/>.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="request">The sent request (should be the same as <see cref="HttpResponseMessage.RequestMessage"/>).</param>
    /// <param name="response">The unsuccessful response.</param>
    /// <returns>The delay to wait before retrying, null to consider this response as the final one.</returns>
    protected virtual Task<TimeSpan?> GetRetryDelayAsync( IActivityMonitor monitor, HttpRequestMessage request, HttpResponseMessage response )
    {
        return Task.FromResult<TimeSpan?>( null );
    }

    /// <summary>
    /// Logs an unexpected <paramref name="response"/> as an error and returns false.
    /// <para>
    /// Nothing else logs a failed response: every failing path must call this (or <see cref="LogResponseAsync"/>)
    /// or the reason for the failure is lost. A status that is a normal answer rather than a failure must
    /// obviously not be reported here.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="response">The unexpected response.</param>
    /// <returns>Always false.</returns>
    protected async Task<bool> LogFailedAsync( IActivityMonitor monitor, HttpResponseMessage response )
    {
        await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
        return false;
    }

    /// <summary>
    /// Logs an unexpected <paramref name="response"/> as an error and returns null.
    /// See <see cref="LogFailedAsync(IActivityMonitor, HttpResponseMessage)"/>.
    /// </summary>
    /// <typeparam name="T">Type of the result of the failing operation.</typeparam>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="response">The unexpected response.</param>
    /// <returns>Always null.</returns>
    protected async Task<T?> LogFailedAsync<T>( IActivityMonitor monitor, HttpResponseMessage response ) where T : class
    {
        await LogResponseAsync( monitor, response, LogLevel.Error ).ConfigureAwait( false );
        return null;
    }

    /// <summary>
    /// Basically emits the response int the <paramref name="monitor"/>.
    /// <para>
    /// Note that logging the request should be avoided: authorization headers, tokens, etc. should not be logged.
    /// </para>
    /// </summary>
    /// <param name="monitor">The target monitor.</param>
    /// <param name="response">The response to log.</param>
    /// <param name="logLevel">The log level.</param>
    /// <returns>The awaitable.</returns>
    protected virtual async Task LogResponseAsync( IActivityMonitor monitor, HttpResponseMessage response, LogLevel logLevel )
    {
        string dumpResponse = await response.Content.ReadAsStringAsync().ConfigureAwait( false );
        monitor.Log( logLevel, $"""
            Request {response.RequestMessage?.Method.Method} '{response.RequestMessage?.RequestUri}' received:
            {response}
            With content:
            {dumpResponse}
            """ );
    }
}
