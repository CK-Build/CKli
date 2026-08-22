using CK.Core;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// The associated <see cref="Roadmap.Publish"/> if the roadmap must be published.
/// </summary>
public sealed partial class PublishRoadmap
{
    readonly Roadmap _roadmap;
    readonly ImmutableArray<RequiredPublish> _requiredPublications;
    readonly IReadOnlyList<RequiredPublish> _alreadyPublished;
    readonly IReadOnlyList<RequiredPublish> _buildingAliens;

    /// <summary>
    /// Gets the roadmap (<see cref="Roadmap.MustPublish"/> is true).
    /// </summary>
    public Roadmap Roadmap => _roadmap;

    /// <summary>
    /// Gets the roadmap's <see cref="PublishableStatus"/>.
    /// </summary>
    public PublishableStatus Status => _roadmap.PublishableStatus;

    /// <summary>
    /// Gets whether the roadmap can be published.
    /// </summary>
    public bool CanPublish => _roadmap.PublishableStatus < PublishableStatus.BuildingPending && _buildingAliens.Count == 0;

    /// <summary>
    /// Gets the roadmap's upstream solutions that are "building/".
    /// When not empty, the <see cref="Status"/> is <see cref="PublishableStatus.BuildingPending"/> and this prevents publishing.
    /// </summary>
    public IEnumerable<Roadmap.BuildSolution> DirectBuildingAliens => _roadmap.OrderedSolutions.Where( s => s.PublishableStatus == PublishableStatus.BuildingPending );

    /// <summary>
    /// Gets the roadmap's upstream solutions that are "building/".
    /// When not empty, the <see cref="Status"/> is <see cref="PublishableStatus.BuildingPending"/> and this prevents publishing.
    /// </summary>
    public IEnumerable<Roadmap.BuildSolution> DirectAlreadyPublished => _roadmap.OrderedSolutions.Where( s => s.PublishableStatus == PublishableStatus.AlreadyPublished );

    /// <summary>
    /// Gets the roadmap's indirect required dependencies that are "building/".
    /// When not empty, this prevents publishing.
    /// </summary>
    public IReadOnlyList<RequiredPublish> IndirectBuildingAliens => _buildingAliens;

    /// <summary>
    /// Gets the roadmap's indirect required dependencies that are already published.
    /// <para>
    /// There should not be any of these: an already published required dependency indicates
    /// an incomplete previous publication. However, these are not considered errors, only warnings.
    /// </para>
    /// </summary>
    public IReadOnlyList<RequiredPublish> IndirectAlreadyPublished => _alreadyPublished;

    /// <summary>
    /// Gets the roadmap's required  dependencies that must be published.
    /// <para>
    /// These are prerequisites to a successful publication ot the roadmap.
    /// </para>
    /// </summary>
    public ImmutableArray<RequiredPublish> IndirectRequiredPublications => _requiredPublications;


    internal IRenderable ToRenderable( ScreenType screen )
    {
        return screen.Text( "Publish Info" );
    }

    internal async Task PublishAsync( IActivityMonitor monitor )
    {
        throw new NotImplementedException();
    }

    PublishRoadmap( Roadmap roadmap,
                    List<RequiredPublish>? alreadyPublished,
                    List<RequiredPublish>? buildingAliens,
                    ImmutableArray<RequiredPublish> requiredPublishes )
    {
        Throw.DebugAssert( roadmap.MustPublish );
        _roadmap = roadmap;
        _alreadyPublished = alreadyPublished ?? [];
        _buildingAliens = buildingAliens ?? [];
        _requiredPublications = requiredPublishes;
    }

    internal static PublishRoadmap? Create( IActivityMonitor monitor, Roadmap roadmap, VersionTagPlugin versionTag )
    {
        // These are warnings.
        List<RequiredPublish>? alreadyPublished = null;
        // These are errors.
        List<RequiredPublish>? buildingAliens = null;
        // These are the regulars.
        ImmutableArray<RequiredPublish> requiredPublishes = [];

        if( roadmap.PublishableStatus == PublishableStatus.IndirectPublishRequired )
        {
            var releaseDB = versionTag.EnsureDatabase( monitor );
            if( releaseDB == null )
            {
                return null;
            }
            var bRequirements = ImmutableArray.CreateBuilder<RequiredPublish>();
            foreach( var s in roadmap.OrderedSolutions )
            {
                if( s.PublishableStatus == PublishableStatus.IndirectPublishRequired )
                {
                    Throw.DebugAssert( s.LastBuild.Version.IsLocal() );
                    var info = releaseDB.GetReleaseInfo( monitor,
                                                         s.LastBuild.TagCommit,
                                                         s.LastBuild.Version.CINumber == 0 && s.LastBuild.TagCommit.CI0Version != null );
                    var consumers = info.GetAllConsumers( monitor );
                    var producers = info.AllProducers;
                    Throw.DebugAssert( !consumers.Overlaps( producers ) );
                    // Among consumers and producers, one can find "local/" or "building/", but there should not be any published version:
                    // - For consumers, it would mean that the published packages rely a non-published one.
                    // - For producers, it would mean that the publication of the producer was not "complete", that some consumers have been missed.
                    // What we do:
                    //   - If we find a "building/", this is a stopper: we consider it (as it is for the direct upstreams), a BuildingPending
                    //     status that is an error.
                    //   - If we find a "local/" (this is the regular case), it must be published.
                    //   - If we find a published version (that SHOULD not happen!), we warn and ignore it.
                    bRequirements.Add( new RequiredPublish( info, info ) );
                    foreach( var c in producers.Concat( consumers ) )
                    {
                        var r = new RequiredPublish( info, c );
                        if( c.Version.IsLocal() )
                        {
                            bRequirements.Add( r );
                        }
                        else if( c.Version.IsBuilding() )
                        {
                            buildingAliens ??= new List<RequiredPublish>();
                            buildingAliens.Add( r );
                        }
                        else
                        {
                            Throw.DebugAssert( c.Version.ParsedPrefix is null );
                            alreadyPublished ??= new List<RequiredPublish>();
                            alreadyPublished.Add( r );
                        }
                    }
                    requiredPublishes = bRequirements.ToImmutable();
                }
            }
        }

        return new PublishRoadmap( roadmap, alreadyPublished, buildingAliens, requiredPublishes );
    }

}

