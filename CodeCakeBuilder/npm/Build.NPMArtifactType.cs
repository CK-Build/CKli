using Cake.Core;
using CK.Text;
using CodeCake.Abstractions;
using CSemVer;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CodeCake
{
    public partial class Build
    {
        /// <summary>
        /// </summary>
        class NPMArtifactType : ArtifactType
        {
            readonly IReadOnlyList<NPMPublishedProject> _projects;

            public NPMArtifactType( StandardGlobalInfo globalInfo )
                : base( globalInfo, "NPM" )
            {
                _projects = NPMSolution.ReadFromNPMSolutionFile( globalInfo.Version ).PublishedProjects;
            }

            public NPMArtifactType( StandardGlobalInfo globalInfo, IEnumerable<NPMPublishedProject> projects )
                : base( globalInfo, "NPM" )
            {
                _projects = projects.ToList();
            }

            protected override IEnumerable<ILocalArtifact> GetLocalArtifacts() => _projects;

            protected override IEnumerable<ArtifactFeed> GetLocalFeeds()
            {
                return new ArtifactFeed[] {
                    new NPMLocalFeed( this, GlobalInfo.LocalFeedPath )
                };
            }

            protected override IEnumerable<ArtifactFeed> GetRemoteFeeds()
            {
                return new NPMRemoteFeed[]{
                        new AzureNPMFeed( this, "Signature-Code", "Default" )
                };
            }

        }

    }
}
