using CK.Core;
using CK.Packaging.Abstractions;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
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

    internal async Task<bool> PublishAsync( IActivityMonitor monitor,
                                            World world,
                                            SVersion profileVersion,
                                            RoadmapPublisher publisher,
                                            IndirectPublisher indirectPublisher,
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

        foreach( var s in _roadmap.OrderedSolutions )
        {
            // Now that any required already built versions are published, we can publish the "current" one.
            if( s.PublishableStatus is PublishableStatus.Build or PublishableStatus.PublishRequired
                && !await publisher.PublishAsync( monitor, s, cancellation ).ConfigureAwait( false ) )
            {
                return false;
            }
        }
        return true;
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
