using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CodeCakeBuilderNPMSolutionFile : XmlFilePluginBase, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly ICommonSolutionSpec _settings;
        readonly SolutionDriver _driver;
        
        public CodeCakeBuilderNPMSolutionFile(
            CodeCakeBuilderFolder f,
            ICommonSolutionSpec settings,
            SolutionDriver driver,
            NormalizedPath branchPath )
            : base( f.Folder, branchPath, f.FolderPath.AppendPart( "NPMSolution.xml" ) )
        {
            _f = f;
            _settings = settings;
            _driver = driver;
       }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var s = _driver.GetPrimarySolution( m );
            if( s == null ) return;
            if( s.NPMProjects.Count == 0 )
            {
                Delete( m );
            }
            else
            {
                var root = new XElement( "NPMSolution", s.NPMProjects.OrderBy( p => p.FullPath ).Select( p => p.ToXml() ) );
                Document = new XDocument( root );
            }
            Save( m );
        }

    }
}
