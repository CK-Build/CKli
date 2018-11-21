using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild.SolutionFiles
{
    public class GitIgnoreFile : GitFolderTextFileBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly ISolutionSettings _settings;
        readonly ILocalFeedProvider _localFeedProvider;

        public GitIgnoreFile( GitFolder f, ISolutionSettings settings, NormalizedPath branchPath )
            : base( f, branchPath.AppendPart( ".gitignore" ) )
        {
            _settings = settings;
            BranchPath = branchPath;
        }

        public NormalizedPath BranchPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

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
