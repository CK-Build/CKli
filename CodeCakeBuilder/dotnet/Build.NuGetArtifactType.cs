using Cake.Common.Solution;
using Cake.Core;
using CodeCake.Abstractions;
using CSemVer;
using System.Collections.Generic;
using System.Linq;

namespace CodeCake
{
    public partial class Build
    {
        /// <summary>
        /// Implements NuGet package handling.
        /// </summary>
        public class NuGetArtifactType : ArtifactType
        {
            readonly IList<SolutionProject> _projectsToPublish;

            public class NuGetArtifact : ILocalArtifact
            {
                public NuGetArtifact( SolutionProject p, SVersion v )
                {
                    Project = p;
                    ArtifactInstance = new ArtifactInstance( "NuGet", p.Name, v );
                }

                public ArtifactInstance ArtifactInstance { get; }

                public SolutionProject Project { get; }
            }

            public NuGetArtifactType( StandardGlobalInfo globalInfo, IEnumerable<SolutionProject> projectsToPublish )
                : base( globalInfo, "NuGet" )
            {
                _projectsToPublish = projectsToPublish.ToList();
            }

            /// <summary>
            /// Gets a mutable list of NuGet artifacts.
            /// </summary>
            /// <param name="reset">True to recompute a list.</param>
            /// <returns>The set of NuGet artifacts.</returns>
            public new IList<NuGetArtifact> GetArtifacts( bool reset = false ) => (IList<NuGetArtifact>)base.GetArtifacts( reset );

            /// <summary>
            /// Gets the remote target feeds.
            /// </summary>
            /// <returns>The set of remote NuGet feeds (in practice at most one).</returns>
            protected override IEnumerable<ArtifactFeed> GetRemoteFeeds()
            {
                return new NuGetHelper.NuGetFeed[]{
                    new SignatureVSTSFeed( this, "Signature-Code", "CKEnvTest3" ),
                };
            }

            /// <summary>
            /// Gets the local target feeds.
            /// </summary>
            /// <returns>The set of remote NuGet feeds (in practice at moste one).</returns>
            protected override IEnumerable<ArtifactFeed> GetLocalFeeds()
            {
                return new NuGetHelper.NuGetFeed[] {
                    new NugetLocalFeed( this, GlobalInfo.LocalFeedPath )
                };
            }

            protected override IEnumerable<ILocalArtifact> GetLocalArtifacts()
            {
                return _projectsToPublish.Select( p => new NuGetArtifact( p, GlobalInfo.Version ) );
            }

        }
    }
}
