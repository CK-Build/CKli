using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugin
{
    public class GitIgnoreFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;

        public GitIgnoreFile( GitFolder f, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( ".gitignore" ) )
        {
            _solutionSpec = solutionSpec;
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
            EnsureLine( GetLines(), "[Bb]in/" );
            EnsureLine( GetLines(), "[Oo]bj/" );
            EnsureLine( GetLines(), "[Rr]elease/" );
            EnsureLine( GetLines(), "[Rr]eleases/" );
            EnsureLine( GetLines(), ".vs/" );
            EnsureLine( GetLines(), "*.suo" );
            EnsureLine( GetLines(), "*.user" );

            if( !_solutionSpec.NoDotNetUnitTests )
            {
                EnsureLine( GetLines(), "Tests/**/TestResult*.xml" );
                EnsureLine( GetLines(), "CodeCakeBuilder/UnitTestsDone.*.txt" );
                EnsureLine( GetLines(), "Tests/**/Logs/" );
                EnsureLine( GetLines(), "Tests/**/CKSetup-WorkingDir/" );
            }
            else
            {
                RemoveLine( GetLines(), "Tests/**/TestResult*.xml" );
                RemoveLine( GetLines(), "CodeCakeBuilder/UnitTestsDone.*.txt" );
                RemoveLine( GetLines(), "Tests/**/Logs/" );
                RemoveLine( GetLines(), "Tests/**/CKSetup-WorkingDir/" );
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

        void RemoveLine( List<string> lines, string line ) => lines.Remove( line );
    }
}
