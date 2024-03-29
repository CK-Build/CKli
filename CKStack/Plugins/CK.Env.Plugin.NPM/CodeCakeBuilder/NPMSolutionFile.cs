using CK.Core;
using System;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public class NPMSolutionFile : XmlFilePluginBase, ICommandMethodsProvider
    {
        readonly NPMCodeCakeBuilderFolder _f;
        readonly NPMProjectsDriver _driver;

        public NPMSolutionFile(
            NPMCodeCakeBuilderFolder f,
            NPMProjectsDriver driver,
            NormalizedPath branchPath )
            : base( f.GitFolder, branchPath, f.FolderPath.AppendPart( "NPMSolution.xml" ), null )
        {
            _f = f;
            _driver = driver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;

            var projects = _driver.GetSimpleNPMProjects( m );
            if( projects == null ) return;

            var workspace = _driver.GetAngularWorkspaces( m );
            if( (projects.Count + (workspace?.Count ?? 0)) == 0 )
            {
                Delete( m );
            }
            else
            {
                var root = new XElement( "NPMSolution",
                    projects.Select( p => p.ToXml() )
                    .Concat(
                        workspace?.OrderBy( p => p.FullPath ).Select( s => s.ToXml() )
                            ?? Enumerable.Empty<XElement>()
                        )
                        
                    );
                Document = new XDocument( root );
            }
            Save( m );
        }

    }
}
