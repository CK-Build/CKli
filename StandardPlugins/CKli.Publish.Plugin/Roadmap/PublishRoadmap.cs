using CK.Core;
using CK.Packaging.Abstractions;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// The associated <see cref="Roadmap"/> publication if the roadmap must be published: the <see cref="FinalProfile"/>
/// that this publication would leave on the roadmap's branch, whether it is publishable, and the publication itself.
/// </summary>
sealed class PublishRoadmap
{
    readonly Roadmap _roadmap;
    readonly PublishedProfileBuilder _gate;
    PublishedProfile? _finalProfile;

    /// <summary>
    /// Gets the roadmap (<see cref="Roadmap.MustPublish"/> is true).
    /// </summary>
    public Roadmap Roadmap => _roadmap;

    /// <summary>
    /// Gets the roadmap's <see cref="PublishableStatus"/>.
    /// </summary>
    public PublishableStatus Status => _roadmap.PublishableStatus;

    /// <summary>
    /// Gets the publication gate: whether the profile this roadmap would leave on its branch is coherent and what
    /// must happen for it to be.
    /// </summary>
    public PublishedProfileBuilder Gate => _gate;

    /// <summary>
    /// Gets the profile this publication carries, available once <see cref="PublishAsync"/> has run.
    /// <para>
    /// On success this is the profile to add to the <see cref="PublishPlugin.PublishedFolder"/>.
    /// </para>
    /// </summary>
    public PublishedProfile? FinalProfile => _finalProfile;

    /// <summary>
    /// Gets whether the roadmap can be published.
    /// </summary>
    public bool CanPublish => _roadmap.PublishableStatus < PublishableStatus.BuildingPending && _gate.IsValid;

    /// <summary>
    /// Gets the roadmap's solutions that are "building/".
    /// When not empty, the <see cref="Status"/> is <see cref="PublishableStatus.BuildingPending"/> and this prevents publishing.
    /// </summary>
    public IEnumerable<Roadmap.BuildSolution> DirectBuildingAliens => _roadmap.OrderedSolutions.Where( s => s.PublishableStatus == PublishableStatus.BuildingPending );

    /// <summary>
    /// Gets the roadmap's solutions that are already published.
    /// </summary>
    public IEnumerable<Roadmap.BuildSolution> DirectAlreadyPublished => _roadmap.OrderedSolutions.Where( s => s.PublishableStatus == PublishableStatus.AlreadyPublished );

    internal IRenderable ToRenderable( ScreenType screen )
    {
        return _gate.ToRenderable( screen, _roadmap ) ?? screen.Unit;
    }

    /// <param name="lease">
    /// The publication lease, null when the command holds none. Each required release is a checkpoint (a renewal
    /// is a no-op until the lease is half over) and it is kept alive while the direct publications run. Losing it
    /// here only warns - a publication cannot be undone, so the releases that follow must still reach their feeds.
    /// The gate that refuses to START without the lock is in <c>PublishPlugin.OnRoadmapBuildAsync</c>.
    /// </param>
    /// <param name="maxDop">The maximal number of repositories published concurrently.</param>
    internal async Task<bool> PublishAsync( IActivityMonitor monitor,
                                            World world,
                                            SVersion profileVersion,
                                            RoadmapPublisher publisher,
                                            IndirectPublisher indirectPublisher,
                                            GitRepository.DistributedLock.Lease? lease,
                                            int maxDop,
                                            CancellationToken cancellation )
    {
        Throw.DebugAssert( CanPublish );
        // The builds are done: the real content is now available, so the profile this publication carries can be
        // built. Nothing is pushed if it has any conflict.
        _finalProfile = _gate.BuildFinalProfile( monitor, _roadmap, world, profileVersion );
        if( _finalProfile == null )
        {
            return false;
        }
        // Everything the profile carries must be on a feed: the "local/" releases from other branches that this
        // publication depends on come first, producers before consumers.
        if( !_gate.RequiredPublications.IsEmpty )
        {
            using( monitor.OpenInfo( $"Publishing {_gate.RequiredPublications.Length} required release(s) from other branches." ) )
            {
                foreach( var release in _gate.RequiredPublications )
                {
                    lease?.KeepAlive( monitor );
                    if( !await indirectPublisher.PublishAsync( monitor, release, cancellation ).ConfigureAwait( false ) )
                    {
                        return false;
                    }
                }
            }
        }
        if( _roadmap.DirectPublishCount == 0 )
        {
            return true;
        }

        // Now that any required already built versions are published, we can publish the "current" ones. They work in
        // the Repos and never touch the Stack repository, which is what makes the concurrent renewal of the lease legal.
        var work = PublishDirectAsync( monitor, publisher, maxDop, cancellation );
        return lease != null
                ? await lease.KeepAliveWhileAsync( monitor, work ).ConfigureAwait( false )
                : await work.ConfigureAwait( false );
    }

    // The atomicity of a publication is per repository: its packages are pushed first (and are never compensated),
    // then its tag, branch and release, each failure of that atomic phase being compensated by the publisher. Across
    // repositories there is no compensation, only an order: a repository is published once all its upstreams have
    // been, and nothing new starts after a failure. This keeps exactly that: each repository waits for the
    // publication of its direct requirements (a requirement that is not published waits for its own ones, so the
    // wait is transitive), at most maxDop of them run at once and a failure closes the start gate.
    //
    // The start gate is never the cancellation given to the publisher: canceling a publication in its atomic phase
    // would skip its compensation (an OperationCanceledException bypasses the UnpublishTag), leaving a pushed tag
    // without release. The publications that are running when a failure occurs complete normally.
    //
    // Each publication runs on its own pooled monitor, and the monitor of the command is the only one bound to the
    // screen: what the publisher would have displayed (warnings, errors and screen tagged infos) is collected and
    // relayed to it once the publication is over, under a lock since the publications end concurrently.
    async Task<bool> PublishDirectAsync( IActivityMonitor monitor, RoadmapPublisher publisher, int maxDop, CancellationToken cancellation )
    {
        var solutions = _roadmap.OrderedSolutions;
        using var _ = monitor.OpenInfo( $"Publishing {_roadmap.DirectPublishCount} repositories (--max-dop {maxDop})." );
        var pool = new ActivityMonitorAsyncPool( maxDop );
        var monitorLock = new Lock();
        using var startGate = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
        var tasks = new Task<bool>[solutions.Length];
        for( int i = 0; i < solutions.Length; ++i )
        {
            var s = solutions[i];
            // The OrderedSolutions are topologically sorted: the requirements' tasks exist.
            var requirements = s.BuildInfo.DirectRequirements.Select( r => tasks[r.Solution.OrderedIndex] ).ToArray();
            Throw.DebugAssert( requirements.All( t => t != null ) );
            tasks[i] = Task.Run( () => PublishOneAsync( s, requirements ) );
        }
        var results = await Task.WhenAll( tasks ).ConfigureAwait( false );
        return results.All( Util.FuncIdentity );

        async Task<bool> PublishOneAsync( Roadmap.BuildSolution s, Task<bool>[] requirements )
        {
            bool upstreamsPublished = (await Task.WhenAll( requirements ).ConfigureAwait( false )).All( Util.FuncIdentity );
            if( s.PublishableStatus is not (PublishableStatus.Build or PublishableStatus.PublishRequired) )
            {
                return upstreamsPublished;
            }
            if( !upstreamsPublished )
            {
                lock( monitorLock )
                {
                    monitor.Warn( $"Publication of '{s.Repo.DisplayPath}' skipped: one of its upstream publications failed." );
                }
                return false;
            }
            using var m = await pool.GetAsync( startGate.Token ).ConfigureAwait( false );
            if( m == null )
            {
                lock( monitorLock )
                {
                    monitor.Warn( cancellation.IsCancellationRequested
                                    ? $"Publication of '{s.Repo.DisplayPath}' canceled."
                                    : $"Publication of '{s.Repo.DisplayPath}' not started: a previous publication failed." );
                }
                return false;
            }
            var relay = new ScreenRelay();
            bool success;
            m.Output.RegisterClient( relay );
            try
            {
                success = await publisher.PublishAsync( m, s, cancellation ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                m.Error( $"While publishing '{s.Repo.DisplayPath}'.", ex );
                success = false;
            }
            finally
            {
                m.Output.UnregisterClient( relay );
            }
            if( !success )
            {
                startGate.Cancel();
            }
            lock( monitorLock )
            {
                relay.RelayTo( monitor );
                if( !success )
                {
                    monitor.Error( $"Unable to publish '{s.Repo.DisplayPath}'." );
                }
            }
            return success;
        }
    }

    // Collects what the ScreenLogger of the command's monitor would have displayed.
    sealed class ScreenRelay : IActivityMonitorClient
    {
        readonly List<(LogLevel Level, CKTrait Tags, string Text, Exception? Exception)> _entries = new();

        public void RelayTo( IActivityMonitor monitor )
        {
            foreach( var (level, tags, text, exception) in _entries )
            {
                monitor.Log( level, tags, text, exception );
            }
        }

        void Collect( ref ActivityMonitorLogData data )
        {
            if( data.MaskedLevel >= LogLevel.Warn || data.Tags.Overlaps( ScreenType.CKliScreenTag ) )
            {
                _entries.Add( (data.MaskedLevel, data.Tags, data.Text, data.Exception) );
            }
        }

        void IActivityMonitorClient.OnUnfilteredLog( ref ActivityMonitorLogData data ) => Collect( ref data );

        void IActivityMonitorClient.OnOpenGroup( IActivityLogGroup group ) => Collect( ref group.Data );

        void IActivityMonitorClient.OnGroupClosing( IActivityLogGroup group, ref List<ActivityLogGroupConclusion>? conclusions ) { }

        void IActivityMonitorClient.OnGroupClosed( IActivityLogGroup group, IReadOnlyList<ActivityLogGroupConclusion> conclusions ) { }

        void IActivityMonitorClient.OnTopicChanged( string newTopic, string? fileName, int lineNumber ) { }

        void IActivityMonitorClient.OnAutoTagsChanged( CKTrait newTrait ) { }
    }

    PublishRoadmap( Roadmap roadmap, PublishedProfileBuilder gate )
    {
        Throw.DebugAssert( roadmap.MustPublish );
        _roadmap = roadmap;
        _gate = gate;
    }

    internal static PublishRoadmap? Create( IActivityMonitor monitor, Roadmap roadmap, VersionTagPlugin versionTag )
    {
        // The gate is computed before any build and is therefore the same in --dry-run: it decides whether the
        // profile this publication would leave on the roadmap's branch is coherent and whether every release it
        // carries can actually reach a feed.
        var gate = PublishedProfileBuilder.Create( monitor, roadmap, versionTag );
        return gate == null ? null : new PublishRoadmap( roadmap, gate );
    }
}
