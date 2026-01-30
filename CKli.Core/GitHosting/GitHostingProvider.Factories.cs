using CK.Core;
using CKli.Core.GitHosting.Providers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace CKli.Core;

public abstract partial class GitHostingProvider // Factory methods.
{
    /// <summary>
    /// <para>
    /// This never throws.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="gitKey">The repository key.</param>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>The hosting provider or null if it cannot be resolved.</returns>
    public static Task<GitHostingProvider?> GetAsync( IActivityMonitor monitor,
                                                      GitRepositoryKey gitKey,
                                                      CancellationToken cancellation = default )
    {
        // Fast path, non async, for KnownGitProvider.
        _providers ??= new Dictionary<(string, bool), GitHostingProvider?>();
        var aKey = gitKey.AccessKey;
        var key = (aKey.PrefixPAT, aKey.IsPublic);
        if( !_providers.TryGetValue( key, out var hosting ) )
        {
            if( aKey.KnownGitProvider != KnownCloudGitProvider.Unknown )
            {
                hosting = aKey.KnownGitProvider switch
                {
                    KnownCloudGitProvider.FileSystem => new FileSystemProvider( aKey ),
                    KnownCloudGitProvider.GitHub => new GitHubProvider( aKey ),
                    _ => Throw.NotSupportedException<GitHostingProvider?>()
                };
            }
            else
            {
                // Slow path.
                return CreateNewAsync( monitor, gitKey, cancellation );
            }
            _providers.Add( key, hosting );
        }
        return Task.FromResult( hosting );
    }

    static async Task<GitHostingProvider?> CreateNewAsync( IActivityMonitor monitor,
                                                           GitRepositoryKey gitKey,
                                                           CancellationToken cancellation )
    {
        Throw.DebugAssert( _providers != null );
        Throw.DebugAssert( gitKey.AccessKey.KnownGitProvider == KnownCloudGitProvider.Unknown );

        GitHostingProvider? result = null;
        try
        {
            // gitKey.OriginUrl.GetLeftPart( UriPartial.Authority ) returns:
            // UriComponents.Scheme | UriComponents.UserInfo | UriComponents.Host | UriComponents.Port in the escaped format.
            //
            // It is a good thing to keep the UriComponents.UserInfo for the base url.
            //
            // However, Uri.Authority returns only UriComponents.Host | UriComponents.Port.
            // We currently keep the UserInfo in our authority but this may not be a good idea.
            //
            var baseUrl = gitKey.OriginUrl.GetLeftPart( UriPartial.Authority );
            Throw.DebugAssert( """
                               The host part is normalized by Uri to be in lower case (https://datatracker.ietf.org/doc/html/rfc9110#section-4.2.3).
                               "The scheme and host are case-insensitive and normally provided in lowercase;"
                               """,
                               baseUrl.ToLowerInvariant() == baseUrl );
            Throw.DebugAssert( baseUrl.StartsWith( "https://" ) && "https://".Length == 8 );
            var authority = baseUrl[..^8];

            result = TryCreateFromUrlPattern( monitor, baseUrl, gitKey, authority );

            if( result == null )
            {
                monitor.Error( $"Unable to resolve hosting provider for '{gitKey.OriginUrl}'." );
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"While resolving hosting provider for '{gitKey.OriginUrl}'.", ex );
        }
        _providers.Add( (gitKey.AccessKey.PrefixPAT, gitKey.AccessKey.IsPublic), result );
        return result;
    }


    static GitHostingProvider? TryCreateFromUrlPattern( IActivityMonitor monitor,
                                                        string baseUrl,
                                                        GitRepositoryKey gitKey,
                                                        string authority )
    {
        if( _pluginResolvers != null )
        {
            // No catch here: if this fails, it must break everything.
            GitHostingProvider? result = null;
            foreach( var provider in _pluginResolvers )
            {
                result = provider.TryCreateFromUrlPattern( monitor, baseUrl, gitKey, authority );
                if( result != null ) return result;
            }
        }
        // Check for GitHub Enterprise pattern (e.g., github.company.com)
        if( authority.Contains( "github", StringComparison.Ordinal ) )
        {
            return new GitHubProvider( baseUrl, gitKey.AccessKey, authority );
        }

        //// Check for Gitea pattern (e.g., gitea.company.com, git.company.com/gitea)
        //if( authority.Contains( "gitea", StringComparison.Ordinal ) )
        //{
        //    return new GiteaProvider( baseUrl, gitKey, authority );
        //}

        //// Check for GitLab pattern (e.g., gitlab.company.com)
        //if( authority.Contains( "gitlab", StringComparison.OrdinalIgnoreCase ) )
        //{
        //    return new GitLabProvider( baseUrl, gitKey, authority );
        //}

        return null;
    }
}
