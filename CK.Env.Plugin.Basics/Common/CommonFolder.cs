using CK.Core;
using CK.Text;
using System.Linq;

namespace CK.Env.Plugin
{
    public class CommonFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _settings;

        public CommonFolder( GitFolder f, SolutionDriver driver, SolutionSpec settings, NormalizedPath branchPath )
            : base( f, branchPath, "Common" )
        {
            _driver = driver;
            _settings = settings;
        }

        protected override void DoApplySettings( IActivityMonitor m )
        {
            var s = _driver.GetSolution( m );
            bool dotNet = s.Projects.Any( p => !p.IsBuildProject && p.Type == ".Net" );
            if( _settings.NoStrongNameSigning || !dotNet )
            {
                DeleteFile( m, "SharedKey.snk" );
            }
            else
            {
                SetBinaryResource( m, "SharedKey.snk" );
            }
        }
    }
}
