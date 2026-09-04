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

/// <summary>
/// Handles the push of NuGet packages to <see cref="NuGetFeed"/> defined
/// by <see cref="ArtifactHandlerPlugin.GetConfiguredNuGetFeeds(IActivityMonitor, out ImmutableArray{NuGetFeed})"/>.
/// </summary>
sealed class PackageSender
{
    readonly ImmutableArray<NuGetFeed> _feeds;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly BranchNamespace _branches;
    readonly ISecretsStore _secretsStore;
    readonly Lock _lock;
    // There can be one NuGet client per feed.
    readonly NuGetFeedClient[] _clients;
    // There can be one Sender per existing branch and its CI.
    readonly Sender[] _senders;

    PackageSender( ImmutableArray<NuGetFeed> feeds, ArtifactHandlerPlugin artifactHandler, BranchModelPlugin branchModel, ISecretsStore secretsStore )
    {
        _feeds = feeds;
        _artifactHandler = artifactHandler;
        _branches = branchModel.BranchNamespace;
        _secretsStore = secretsStore;
        _clients = new NuGetFeedClient[_feeds.Length];
        _senders = new Sender[2 * _branches.Branches.Length];
        _lock = new Lock();
    }

    /// <summary>
    /// Sens the produced package names with the provided version.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version of the packages.</param>
    /// <param name="packageNames">The NuGet package names.</param>
    /// <param name="cancel">Cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> SendAsync( IActivityMonitor monitor, SVersion version, ImmutableArray<string> packageNames, CancellationToken cancel )
    {
        var b = _branches.FindRequired( monitor, version );
        if( b == null ) return Task.FromResult( false );
        var s = EnsureSender( monitor, b, version.IsCI );
        if( s == null ) return Task.FromResult( false );
        return s.SendAsync( monitor, version, packageNames, cancel );
    }

    NuGetFeedClient? EnsureClient( IActivityMonitor monitor, NuGetFeed feed )
    {
        Throw.DebugAssert( _feeds.Contains( feed ) && feed.Credentials != null );
        int idx = _feeds.IndexOf( feed );
        ref var c = ref _clients[idx];
        if( c == null )
        {
            var apiKey = _secretsStore.TryGetRequiredSecret( monitor, feed.Credentials.SecretKey );
            if( apiKey != null )
            {
                c = new NuGetFeedClient( feed.Url, apiKey );
            }
        }
        return c;
    }

    Sender? EnsureSender( IActivityMonitor monitor, BranchName branch, bool isCI )
    {
        int idx = branch.Index;
        if( isCI ) ++idx;
        ref var sender = ref _senders[idx];
        if( sender == null )
        {
            lock( _lock )
            {
                if( sender == null )
                {
                    var clients = new List<NuGetFeedClient>();
                    foreach( var f in _feeds.Where( f => f.CanPush( branch.VersionKind, isCI ) ) )
                    {
                        var client = EnsureClient( monitor, f );
                        if( client == null )
                        {
                            return null;
                        }
                        var apiKey = _secretsStore.TryGetRequiredSecret( monitor, f.Credentials!.SecretKey );
                        if( apiKey == null )
                        {
                            return null;
                        }
                        clients.Add( client );
                    }
                    if( clients.Count == 0 )
                    {
                        var name = $"'{branch.VersionKind.ToKindName()}' versions";
                        if( isCI ) name = "ci build of " + name;
                        monitor.Error( $"No configured NuGet feeds with PushCredentials accept {name}." );
                        return null;
                    }
                    sender = new Sender( _artifactHandler, clients );
                }
            }
        }
        return sender;
    }

    /// <summary>
    /// Creates a <see cref="PackageSender"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="artifactHandler">The artifact handler.</param>
    /// <param name="branchModel">The branch model.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <returns>The sender or null on error.</returns>
    public static PackageSender? Create( IActivityMonitor monitor, ArtifactHandlerPlugin artifactHandler, BranchModelPlugin branchModel, ISecretsStore secretsStore )
    {
        if( !artifactHandler.GetConfiguredNuGetFeeds( monitor, out var feeds ) )
        {
            return null;
        }
        return new PackageSender( feeds, artifactHandler, branchModel, secretsStore );
    }


    /// <summary>
    /// A Sender can send packages with the same <see cref="CSVersionKind"/> and CI flag to one or more NuGet client.
    /// </summary>
    sealed class Sender
    {
        readonly ArtifactHandlerPlugin _artifactHandler;
        readonly List<NuGetFeedClient> _clients;

        public Sender( ArtifactHandlerPlugin artifactHandler, List<NuGetFeedClient> clients )
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

    }

}

