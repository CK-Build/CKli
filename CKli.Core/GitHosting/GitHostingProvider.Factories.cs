using CK.Core;
using CKli.Core.GitHosting.Providers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        // Fast path, non async for KnownGitProvider.
        _providers ??= new Dictionary<(string, bool), GitHostingProvider?>();
        var key = (gitKey.PrefixPAT, gitKey.IsPublic);
        if( !_providers.TryGetValue( key, out var hosting ) )
        {
            if( gitKey.KnownGitProvider != KnownCloudGitProvider.Unknown )
            {
                hosting = gitKey.KnownGitProvider switch
                {
                    KnownCloudGitProvider.FileSystem => new FileSystemProvider( gitKey ),
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
        GitHostingProvider? result = null;
        try
        {
            throw new NotImplementedException();
        }
        catch( Exception ex )
        {
            monitor.Error( $"While resolving hosting provider for '{gitKey.OriginUrl}'.", ex );
        }
        _providers.Add( (gitKey.PrefixPAT, gitKey.IsPublic), result );
        return result;
    }
}
