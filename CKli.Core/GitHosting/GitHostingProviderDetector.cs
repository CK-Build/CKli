using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;
using CKli.Core.GitHosting.Providers;

namespace CKli.Core;

/// <summary>
/// Detects and creates Git hosting providers on-demand based on remote URLs.
/// </summary>
public static class GitHostingProviderDetector
{
    static readonly HttpClient s_httpClient = CreateHttpClient();

    static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds( 10 ) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd( "CKli-GitHosting/1.0" );
        return client;
    }

    /// <summary>
    /// Creates a provider for a specific host without detection.
    /// Use this when you know the provider type. This is required for creating <see cref="GitHostingType.FileSystem"/>
    /// providers to anchor them 
    /// </summary>
    /// <param name="provider">The type of provider to create.</param>
    /// <param name="host">The hostname.</param>
    /// <param name="secretsStore">The secrets store for credential retrieval.</param>
    /// <returns>The created provider, or null if the provider type is unknown.</returns>
    public static IGitHostingProvider? CreateProvider( GitHostingType provider, string host, ISecretsStore secretsStore )
    {
        return provider switch
        {
            //GitHostingType.GitHub => new GitHubProvider(
            //    host,
            //    string.Equals( host, "github.com", StringComparison.OrdinalIgnoreCase )
            //        ? new Uri( "https://api.github.com/" )
            //        : new Uri( $"https://{host}/api/v3/" ),
            //    secretsStore ),

            //GitHostingType.GitLab => new GitLabProvider(
            //    host,
            //    string.Equals( host, "gitlab.com", StringComparison.OrdinalIgnoreCase )
            //        ? new Uri( "https://gitlab.com/api/v4/" )
            //        : new Uri( $"https://{host}/api/v4/" ),
            //    secretsStore ),

            //GitHostingType.Gitea => new GiteaProvider(
            //    host,
            //    new Uri( $"https://{host}/api/v1/" ),
            //    secretsStore ),

            //GitHostingType.FileSystem => new GiteaProvider(
            //    host,
            //    new Uri( $"https://{host}/api/v1/" ),
            //    secretsStore ),

            _ => null
        };
    }

    /// <summary>
    /// Resolves and creates a provider for the given remote URL.
    /// Once obtained or a <see cref="IGitHostingProvider.HostName"/>, the provider is cached.
    /// </summary>
    /// <remarks>
    /// <para><b>Detection Strategy (4 stages):</b></para>
    /// <list type="number">
    /// <item><b>Well-Known Hostnames:</b> Instant detection for known cloud providers
    /// (github.com → GitHub, gitlab.com → GitLab).</item>
    /// <item><b>Hostname Patterns:</b> Heuristic matching based on hostname substrings
    /// (contains "gitea" → Gitea, contains "gitlab" → GitLab, contains "github" → GitHub Enterprise).</item>
    /// <item><b>API Sniffing:</b> Network requests to provider-specific endpoints to identify the provider type.
    /// Note: This may fail for instances behind authentication or reverse proxies.</item>
    /// <item><b>Try-All Fallback:</b> Attempts each provider type sequentially using available credentials.
    /// Requires valid PATs to identify the correct provider.</item>
    /// </list>
    /// <para><b>Detection Limitations:</b></para>
    /// <list type="bullet">
    /// <item><b>Gitea:</b> <c>GET /api/v1/version</c> typically requires authentication.
    /// Detection without credentials will fail for most Gitea instances.</item>
    /// <item><b>Enterprise/Self-Hosted:</b> Instances behind Cloudflare or reverse proxies
    /// may not respond to API sniffing. Use hostnames containing provider hints
    /// (e.g., "gitea.company.com") for reliable detection.</item>
    /// <item><b>GitLab Self-Hosted:</b> Some instances disable <c>/api/v4/version</c>
    /// for unauthenticated requests.</item>
    /// </list>
    /// </remarks>
    /// <param name="monitor">The activity monitor.</param>
    /// <param name="secretsStore">The secrets store for credential retrieval.</param>
    /// <param name="remoteUrl">The remote repository URL (HTTPS or SSH format).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved provider, or null if detection failed.</returns>
    public static async Task<IGitHostingProvider?> ResolveProviderAsync( IActivityMonitor monitor,
                                                                         ISecretsStore secretsStore,
                                                                         string remoteUrl,
                                                                         CancellationToken ct = default )
    {
        var host = GitHosting.RemoteUrlParser.GetHost( remoteUrl );
        if( string.IsNullOrEmpty( host ) )
        {
            monitor.Error( $"Could not extract host from remote URL: {remoteUrl}" );
            return null;
        }

        using( monitor.OpenInfo( $"Detecting Git hosting provider for '{host}'..." ) )
        {
            // Stage 1: Well-known hostnames
            var provider = TryCreateFromWellKnownHost( host, secretsStore );
            if( provider != null )
            {
                monitor.Info( $"Detected well-known host: {provider.GetType().Name}" );
                return provider;
            }

            // Stage 2: Hostname pattern matching
            provider = TryCreateFromHostnamePattern( host, secretsStore );
            if( provider != null )
            {
                monitor.Info( $"Detected from hostname pattern: {provider.GetType().Name}" );
                return provider;
            }

            // Stage 3: API sniffing
            provider = await TryDetectViaApiSniffingAsync( monitor, host, secretsStore, ct );
            if( provider != null )
            {
                monitor.Info( $"Detected via API sniffing: {provider.GetType().Name}" );
                return provider;
            }

            // Stage 4: Try-all fallback
            var parsed = GitHosting.RemoteUrlParser.ParseStandardPath( GitHosting.RemoteUrlParser.TryNormalizeToHttps( remoteUrl )?.AbsolutePath ?? "" );
            if( parsed == null )
            {
                monitor.Error( $"Could not parse owner/repo from URL: {remoteUrl}" );
                return null;
            }

            provider = await TryAllProvidersAsync( monitor, host, parsed.Value.Owner, parsed.Value.RepoName, secretsStore, ct );
            if( provider != null )
            {
                monitor.Info( $"Detected via try-all fallback: {provider.GetType().Name}" );
                return provider;
            }

            monitor.Error( $"""
                        Could not detect Git hosting provider for '{host}'. 
                        Ensure the hostname contains a provider hint (e.g., 'gitea.company.com') or that valid credentials are configured.
                        """ );
            return null;
        }
    }

    /// <summary>
    /// Creates a provider for a well-known cloud host.
    /// </summary>
    static IGitHostingProvider? TryCreateFromWellKnownHost( string host, ISecretsStore secretsStore )
    {
        //if( string.Equals( host, "github.com", StringComparison.OrdinalIgnoreCase ) )
        //{
        //    return new GitHubProvider( "github.com", new Uri( "https://api.github.com/" ), secretsStore );
        //}

        //if( string.Equals( host, "gitlab.com", StringComparison.OrdinalIgnoreCase ) )
        //{
        //    return new GitLabProvider( "gitlab.com", new Uri( "https://gitlab.com/api/v4/" ), secretsStore );
        //}

        return null;
    }

    /// <summary>
    /// Creates a provider based on hostname patterns.
    /// </summary>
    static IGitHostingProvider? TryCreateFromHostnamePattern( string host, ISecretsStore secretsStore )
    {
        //// Check for Gitea pattern (e.g., gitea.company.com, git.company.com/gitea)
        //if( host.Contains( "gitea", StringComparison.OrdinalIgnoreCase ) )
        //{
        //    return new GiteaProvider( host, new Uri( $"https://{host}/api/v1/" ), secretsStore );
        //}

        //// Check for GitLab pattern (e.g., gitlab.company.com)
        //if( host.Contains( "gitlab", StringComparison.OrdinalIgnoreCase ) )
        //{
        //    return new GitLabProvider( host, new Uri( $"https://{host}/api/v4/" ), secretsStore );
        //}

        //// Check for GitHub Enterprise pattern (e.g., github.company.com)
        //if( host.Contains( "github", StringComparison.OrdinalIgnoreCase ) )
        //{
        //    return new GitHubProvider( host, new Uri( $"https://{host}/api/v3/" ), secretsStore );
        //}

        return null;
    }

    /// <summary>
    /// Attempts to detect the provider by making API requests.
    /// </summary>
    static async Task<IGitHostingProvider?> TryDetectViaApiSniffingAsync(
        IActivityMonitor monitor,
        string host,
        ISecretsStore secretsStore,
        CancellationToken ct )
    {
        //// Try GitHub (check for X-GitHub-Request-Id header or /zen endpoint)
        //if( await IsGitHubAsync( host, ct ) )
        //{
        //    // Determine if it's github.com or GitHub Enterprise
        //    var isGitHubCom = string.Equals( host, "github.com", StringComparison.OrdinalIgnoreCase );
        //    var apiUrl = isGitHubCom
        //        ? new Uri( "https://api.github.com/" )
        //        : new Uri( $"https://{host}/api/v3/" );
        //    return new GitHubProvider( host, apiUrl, secretsStore );
        //}

        //// Try GitLab (check /api/v4/version endpoint)
        //if( await IsGitLabAsync( host, ct ) )
        //{
        //    return new GitLabProvider( host, new Uri( $"https://{host}/api/v4/" ), secretsStore );
        //}

        //// Gitea detection via /api/v1/version usually requires auth
        //// We'll skip it here and rely on try-all fallback
        //monitor.Trace( $"API sniffing inconclusive for '{host}'. Gitea detection typically requires authentication." );

        return null;
    }

    /// <summary>
    /// Checks if the host is a GitHub instance by looking for GitHub-specific headers.
    /// </summary>
    static async Task<bool> IsGitHubAsync( string host, CancellationToken ct )
    {
        try
        {
            var baseUrl = string.Equals( host, "github.com", StringComparison.OrdinalIgnoreCase )
                ? "https://api.github.com"
                : $"https://{host}/api/v3";

            using var request = new HttpRequestMessage( HttpMethod.Get, $"{baseUrl}/zen" );
            using var response = await s_httpClient.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, ct );

            // GitHub returns X-GitHub-Request-Id header
            return response.Headers.Contains( "X-GitHub-Request-Id" );
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the host is a GitLab instance by calling the version endpoint.
    /// </summary>
    static async Task<bool> IsGitLabAsync( string host, CancellationToken ct )
    {
        try
        {
            using var response = await s_httpClient.GetAsync( $"https://{host}/api/v4/version", ct );

            // GitLab returns 200 with version info, or 401 if auth required (but still GitLab)
            // Check for GitLab-specific response patterns
            if( response.IsSuccessStatusCode )
            {
                var content = await response.Content.ReadAsStringAsync( ct );
                return content.Contains( "version", StringComparison.OrdinalIgnoreCase ) &&
                       content.Contains( "revision", StringComparison.OrdinalIgnoreCase );
            }

            // 401 with specific header indicates GitLab requiring auth
            if( response.StatusCode == System.Net.HttpStatusCode.Unauthorized )
            {
                // GitLab typically returns WWW-Authenticate header
                return response.Headers.Contains( "WWW-Authenticate" );
            }
        }
        catch
        {
            // Ignore - host is not GitLab
        }
        return false;
    }

    /// <summary>
    /// Attempts all provider types sequentially using available credentials.
    /// </summary>
    static async Task<IGitHostingProvider?> TryAllProvidersAsync(
        IActivityMonitor monitor,
        string host,
        string owner,
        string repoName,
        ISecretsStore secretsStore,
        CancellationToken ct )
    {
        monitor.Info( $"Trying all providers for '{host}' with available credentials..." );

        //// Create providers for this host
        //var providers = new IGitHostingProvider[]
        //{
        //    new GitHubProvider( host, new Uri( $"https://{host}/api/v3/" ), secretsStore ),
        //    new GitLabProvider( host, new Uri( $"https://{host}/api/v4/" ), secretsStore ),
        //    new GiteaProvider( host, new Uri( $"https://{host}/api/v1/" ), secretsStore )
        //};

        //foreach( var provider in providers )
        //{
        //    try
        //    {
        //        var result = await provider.GetRepositoryInfoAsync( monitor, owner, repoName, ct );

        //        // Success or 404 (repo not found but provider is correct) indicates we found the provider
        //        if( result.Success || result.IsNotFound )
        //        {
        //            monitor.Trace( $"Provider {provider.GetType().Name} responded successfully for '{host}'." );
        //            // Dispose other providers
        //            foreach( var p in providers )
        //            {
        //                if( p != provider ) p.Dispose();
        //            }
        //            return provider;
        //        }

        //        // Auth error means wrong credentials or wrong provider - continue trying
        //        if( result.IsAuthenticationError )
        //        {
        //            monitor.Trace( $"Provider {provider.GetType().Name} returned auth error for '{host}'. Trying next..." );
        //        }
        //    }
        //    catch( Exception ex )
        //    {
        //        monitor.Trace( $"Provider {provider.GetType().Name} failed for '{host}': {ex.Message}" );
        //    }
        //}

        //// No provider worked - dispose all
        //foreach( var p in providers ) p.Dispose();
        return null;
    }

}
