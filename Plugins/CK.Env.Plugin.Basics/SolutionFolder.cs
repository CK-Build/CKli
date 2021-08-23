using CK.Core;
using CK.Text;
using System;

namespace CK.Env.Plugin
{
    public class SolutionFolder : PluginFolderBase
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;

        public SolutionFolder( GitRepository f, SolutionDriver driver, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath, subFolderPath: String.Empty, "Basics/Res" )
        {
            _driver = driver;
            _solutionSpec = solutionSpec;
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
