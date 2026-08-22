using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

//sealed class PackageSenderResolver
//{
//    readonly ImmutableArray<NuGetFeed> _feeds;
//    readonly ArtifactHandlerPlugin _artifactHandler;
//    readonly BranchNamespace _branches;
//    readonly ISecretsStore _secretsStore;
//    readonly PackageSender[] _senders;

//    public PackageSenderResolver( ImmutableArray<NuGetFeed> feeds, ArtifactHandlerPlugin artifactHandler, BranchModelPlugin branchModel, ISecretsStore secretsStore )
//    {
//        _feeds = feeds;
//        _artifactHandler = artifactHandler;
//        _branches = branchModel.BranchNamespace;
//        _secretsStore = secretsStore;
//        _senders = new PackageSender[2 * _branches.Branches.Length];
//    }

//    public async Task<bool> SendAsync( IActivityMonitor monitor, SVersion version, ImmutableArray<string> packageNames, CancellationToken cancel )
//    {
//        var b = _branches.FindRequired( monitor, version );
//        if( b == null ) return false;

//        int idx = b.Index;
//        if( version.IsCI ) ++idx;

//        var sender = _senders[idx];
//        if( sender == null )
//        {
//            var clients = new List<NuGetFeedClient>();
//            foreach( var f in _feeds.Where( f => f.PushCredentials != null
//                                                && f.PushCredentials.IsAPIKey
//                                                && (!version.IsCI || f.PushQualityFilter.AllowCI) ) )
//            {
//                if( f.PushQualityFilter.Accepts( version.Pre ) )
//                {
//                    var apiKey = secretsStore.TryGetRequiredSecret( monitor, f.PushCredentials!.SecretKey );
//                    if( apiKey == null )
//                    {
//                        return null;
//                    }
//                    clients.Add( new NuGetFeedClient( f.Url, apiKey ) );
//                }
//            }
//            if( clients.Count == 0 )
//            {
//                var name = prereleaseName.Length > 0 ? $"'{prereleaseName}' versions" : "stable versions";
//                if( ciBuild ) name = "ci build of " + name;
//                monitor.Error( $"No configured NuGet feeds with PushCredentials accept {name}." );
//                return null;
//            }
//        }

//    }

//    public static PackageSenderResolver? Create( IActivityMonitor monitor, ArtifactHandlerPlugin artifactHandler, BranchModelPlugin branchModel, ISecretsStore secretsStore )
//    {
//        if( !artifactHandler.GetConfiguredNuGetFeeds( monitor, out var feeds ) )
//        {
//            return null;
//        }
//        return new PackageSenderResolver( feeds, artifactHandler, branchModel, secretsStore );
//    }
//}

sealed class PackageSender
{
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly List<NuGetFeedClient> _clients;

    PackageSender( ArtifactHandlerPlugin artifactHandler, List<NuGetFeedClient> clients )
    {
        _artifactHandler = artifactHandler;
        _clients = clients;
    }

    public async Task<bool> SendAsync( IActivityMonitor monitor, SVersion version, ImmutableArray<string> packageNames, CancellationToken cancel )
    {
        var fileNames = packageNames.Select( p => _artifactHandler.LocalNuGetPath.AppendPart( $"{p}.{version}.nupkg" ) ).ToArray();
        using( monitor.OpenInfo( $"Pushing {fileNames.Length} packages to {_clients.Count} feeds." ) )
        {
            // Parallel only by client.
            var allTasks = _clients.Select( c => c.PushAsync( monitor.ParallelLogger, fileNames.Select( p => p.Path ), skipDuplicate: true, cancel ) ).ToArray();
            var results = await Task.WhenAll( allTasks ).ConfigureAwait( false );

            // Generic parallel error handling.
            var failed = results.Count( success => !success );
            if( failed > 0 )
            {
                monitor.Error( $"{failed} errors out of {results.Length} ." );
            }
            return failed == 0;
        }
    }

    /// <summary>
    /// Selects and configure NuGet clients to which packages must be sent and combine them in a <see cref="PackageSender"/>.
    /// <para>
    /// Filters the feeds provided by <see cref="ArtifactHandlerPlugin.GetConfiguredNuGetFeeds(IActivityMonitor, out ImmutableArray{NuGetFeed})"/>,
    /// to the ones with a <see cref="NuGetFeed.PushCredentials"/> that has a true <see cref="NuGetFeedCredentials.IsAPIKey"/> and which <see cref="NuGetFeed.PushQualityFilter"/>
    /// accepts the <paramref name="prereleaseName"/> and <paramref name="ciBuild"/> flag.
    /// </para>
    /// <para>
    /// If no such feed exist, this is an error and null is returned.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="prereleaseName">The prerelease name. See <see cref="SVersionQualityFilter.AcceptsPreleaseName(ReadOnlySpan{char})"/>.</param>
    /// <param name="ciBuild">Whether the packages to publish are CI builds. See <see cref="SVersion.IsCI"/>.</param>
    /// <param name="artifactHandler">The artifact handler plugin.</param>
    /// <param name="secretsStore">The secret store.</param>
    /// <returns>A package sender.</returns>
    public static PackageSender? Create( IActivityMonitor monitor,
                                         ReadOnlySpan<char> prereleaseName,
                                         bool ciBuild,
                                         ArtifactHandlerPlugin artifactHandler,
                                         ISecretsStore secretsStore )
    {
        if( !artifactHandler.GetConfiguredNuGetFeeds( monitor, out var feeds ) )
        {
            return null;
        }
        var clients = new List<NuGetFeedClient>();
        foreach( var f in feeds.Where( f => f.PushCredentials != null
                                            && f.PushCredentials.IsAPIKey
                                            && (!ciBuild || f.PushQualityFilter.AllowCI) ) )
        {
            if( f.PushQualityFilter.AcceptsPreleaseName( prereleaseName ) )
            {
                var apiKey = secretsStore.TryGetRequiredSecret( monitor, f.PushCredentials!.SecretKey );
                if( apiKey == null )
                {
                    return null;
                }
                clients.Add( new NuGetFeedClient( f.Url, apiKey ) );
            }
        }
        if( clients.Count == 0 )
        {
            var name = prereleaseName.Length > 0 ? $"'{prereleaseName}' versions" : "stable versions";
            if( ciBuild ) name = "ci build of " + name;
            monitor.Error( $"No configured NuGet feeds with PushCredentials accept {name}." );
            return null;
        }
        return new PackageSender( artifactHandler, clients );
    }

}

