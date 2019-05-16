using CK.Core;
using CK.Text;
using System.Collections.Generic;
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
            : base( f.Folder, branchPath, f.FolderPath.AppendPart( "NPMSolution.xml" ) )
        {
            _f = f;
            _driver = driver;
       }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var projects = _driver.GetNPMProjects( m );
            if( projects == null ) return;
            if( projects.Count == 0 )
            {
                Delete( m );
            }
            else
            {
                var root = new XElement( "NPMSolution", projects.OrderBy( p => p.FullPath ).Select( p => p.ToXml() ) );
                Document = new XDocument( root );
            }
            Save( m );
        }

    }
}
