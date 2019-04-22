using CK.Core;
using CK.Text;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CommonFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;
        readonly ISolutionSettings _settings;

        public CommonFolder( GitFolder f, SolutionDriver driver, ISolutionSettings settings, NormalizedPath branchPath )
            : base( f, branchPath, "Common" )
        {
            _driver = driver;
            _settings = settings;
        }

        /// <summary>
        /// Gets whether .Net packages are published.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>Null on error, true if at least one .Net packages is produced, false otherwise.</returns>
        public bool? HasDotnetPublishedProjects( IActivityMonitor m ) => _driver.HasDotnetPublishedProjects( m );

        protected override void DoApplySettings( IActivityMonitor m )
        {
            bool? useDotnet = HasDotnetPublishedProjects( m );
            if( useDotnet == null ) return;

            if( _settings.NoStrongNameSigning || useDotnet == false )
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
