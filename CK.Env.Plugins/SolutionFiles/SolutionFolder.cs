using CK.Core;
using CK.Text;
using System;

namespace CK.Env.Plugins.SolutionFiles
{
    public class SolutionFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;
        readonly ICommonSolutionSpec _settings;

        public SolutionFolder( GitFolder f, SolutionDriver driver, ICommonSolutionSpec settings, NormalizedPath branchPath )
            : base( f, branchPath, folderPath: String.Empty )
        {
            _driver = driver;
            _settings = settings;
        }

        /// <summary>
        /// Gets the name of this command: it is "<see cref="FolderPath"/>(Folder)".
        /// </summary>
        /// <returns>The command name.</returns>
        protected override NormalizedPath GetCommandProviderName() => FolderPath.AppendPart( "(Folder)" );


        protected override void DoApplySettings( IActivityMonitor m )
        {
            SetTextResource( m, ".editorconfig" );
        }
    }
}
