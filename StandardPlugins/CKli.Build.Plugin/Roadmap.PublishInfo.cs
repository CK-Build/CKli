using CK.Core;
using CKli.VersionTag.Plugin;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Captures an indirect required publication: the the <see cref="Origin"/> is a "local/" <see cref="BuildSolution.LastBuild"/>
    /// that is not on the <see cref="HotGraph.BranchName"/> and the <see cref="Required"/> is also a "local/".
    /// Required can be the Origin itself, one of its consumer or a producer.
    /// </summary>
    /// <param name="Origin">The initially referenced release by the roadmap's pivots.</param>
    /// <param name="Required">The "local/" Repo/Version that must be published.</param>
    public readonly record struct RequiredPublish( RepoReleaseInfo Origin, RepoReleaseInfo Required )
    {
        /// <summary>
        /// Gets whether the <see cref="Required"/> is a producer of this <see cref="Origin"/>.
        /// If it's not a producer that it can be a consumer or the Origin itself.
        /// </summary>
        public bool IsProducer => Origin.AllProducers.Contains( Required );
    }

    /// <summary>
    /// The associated <see cref="Roadmap.Publish"/> if the roadmap must be published.
    /// </summary>
    public sealed class PublishInfo
    {
        readonly Roadmap _roadmap;
        readonly ImmutableArray<RequiredPublish> _requiredPublications;
        readonly IReadOnlyList<RequiredPublish> _alreadyPublished;
        readonly IReadOnlyList<RequiredPublish> _buildingAliens;

        PublishInfo( Roadmap roadmap,
                     List<RequiredPublish>? alreadyPublished,
                     List<RequiredPublish>? buildingAliens,
                     ImmutableArray<RequiredPublish> requiredPublishes )
        {
            _roadmap = roadmap;
            _alreadyPublished = alreadyPublished ?? [];
            _buildingAliens = buildingAliens ?? [];
            _requiredPublications = requiredPublishes;
        }

        /// <summary>
        /// Gets the roadmap's <see cref="PublishableStatus"/>.
        /// </summary>
        public PublishableStatus Status => _roadmap.Publishable;

        /// <summary>
        /// Gets whether the roadmap can be published.
        /// </summary>
        public bool CanPublish => _roadmap.Publishable < PublishableStatus.BuildingPending && _buildingAliens.Count == 0;

        /// <summary>
        /// Gets the roadmap's upstream solutions that are "building/".
        /// When not empty, the <see cref="Status"/> is <see cref="PublishableStatus.BuildingPending"/> and this prevents publishing.
        /// </summary>
        public IEnumerable<BuildSolution> DirectBuildingAliens => _roadmap.OrderedSolutions.Where( s => s.Publishable == PublishableStatus.BuildingPending );

        /// <summary>
        /// Gets the roadmap's upstream solutions that are "building/".
        /// When not empty, the <see cref="Status"/> is <see cref="PublishableStatus.BuildingPending"/> and this prevents publishing.
        /// </summary>
        public IEnumerable<BuildSolution> DirectAlreadyPublished => _roadmap.OrderedSolutions.Where( s => s.Publishable == PublishableStatus.AlreadyPublished );

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



        internal static PublishInfo? Create( IActivityMonitor monitor, Roadmap roadmap, VersionTagPlugin versionTag )
        {
            // These are warnings.
            List<RequiredPublish>? alreadyPublished = null;
            // These are errors.
            List<RequiredPublish>? buildingAliens = null;
            // These are the regulars.
            ImmutableArray<RequiredPublish> requiredPublishes = [];

            if( roadmap._publishable == PublishableStatus.PublishRequiredBaseBranch )
            {
                var releaseDB = versionTag.EnsureDatabase( monitor );
                if( releaseDB == null )
                {
                    return null;
                }
                var bRequirements = ImmutableArray.CreateBuilder<RequiredPublish>();
                foreach( var s in roadmap._orderedSolutions )
                {
                    if( s.Publishable == PublishableStatus.PublishRequiredBaseBranch )
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

            return new PublishInfo( roadmap, alreadyPublished, buildingAliens, requiredPublishes );
        }

    }

}
