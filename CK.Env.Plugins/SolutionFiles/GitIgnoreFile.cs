using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class GitIgnoreFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly ISolutionSettings _settings;

        public GitIgnoreFile( GitFolder f, ISolutionSettings settings, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( ".gitignore" ) )
        {
            _settings = settings;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            List<string> lines = null;

            List<string> GetLines() => lines ?? (lines = TextContent.NormalizeEOLToCRLF()
                                                   .Split( new[] { "\r\n" }, StringSplitOptions.None )
                                                   .ToList());
            if( !_settings.NoUnitTests )
            {
                EnsureLine( GetLines(), "Tests/**/TestResult*.xml" );
                EnsureLine( GetLines(), "CodeCakeBuilder/UnitTestsDone.*.txt" );
                EnsureLine( GetLines(), "Tests/**/Logs/" );
                EnsureLine( GetLines(), "Tests/**/CKSetup-WorkingDir/" );
            }
            if( lines != null )
            {
                CreateOrUpdate( m, lines.Concatenate( "\r\n" ) );
            }
        }

        void EnsureLine( List<string> lines, string line )
        {
            if( !lines.Contains( line ) ) lines.Add( line );
        }
    }
}
