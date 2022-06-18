using CK.Core;
using CK.Env.MSBuildSln;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.Plugin
{
    public class GitIgnoreFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;
        readonly SolutionDriver _driver;

        public GitIgnoreFile( GitRepository f, SolutionDriver driver, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( ".gitignore" ) )
        {
            _solutionSpec = solutionSpec;
            _driver = driver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor monitor )
        {
            var solution = _driver.GetSolution( monitor, allowInvalidSolution: true );
            if( solution != null )
            {
                // Update the "Solution Items" folder.
                solution.Tag<SolutionFile>()?.EnsureSolutionItemFile( monitor, FilePath.RemovePrefix( BranchPath ) );
            }

            List<string> lines = TextContent != null
                                    ? TextContent
                                        .Split( new[] { "\r\n" }, StringSplitOptions.None )
                                        .ToList()
                                    : new List<string>();
            EnsureLine( lines, "[Bb]in/" );
            EnsureLine( lines, "[Oo]bj/" );
            EnsureLine( lines, "[Rr]elease/" );
            EnsureLine( lines, "[Rr]eleases/" );
            RemoveLine( lines, "[Rr]elease/" );
            RemoveLine( lines, "[Rr]eleases/" );
            EnsureLine( lines, "CodeCakeBuilder/[Rr]elease/" );
            EnsureLine( lines, "CodeCakeBuilder/[Rr]eleases/" );
            EnsureLine( lines, ".vs/" );
            EnsureLine( lines, "*.suo" );
            EnsureLine( lines, "*.user" );
            EnsureLine( lines, "CodeCakeBuilder/UnitTestsDone.*.txt" );
            EnsureLine( lines, "$StObjGen/" );

            if( !_solutionSpec.NoDotNetUnitTests )
            {
                EnsureLine( lines, "Tests/**/TestResult*.xml" );
                EnsureLine( lines, "CodeCakeBuilder/MemoryKey.*.txt" );
                EnsureLine( lines, "Tests/**/Logs/" );
                EnsureLine( lines, "Tests/**/CKSetup-WorkingDir/" );
            }
            else
            {
                RemoveLine( lines, "Tests/**/TestResult*.xml" );
                RemoveLine( lines, "CodeCakeBuilder/MemoryKey.*.txt" );
                RemoveLine( lines, "Tests/**/Logs/" );
                RemoveLine( lines, "Tests/**/CKSetup-WorkingDir/" );
            }
            if( lines != null )
            {
                CreateOrUpdate( monitor, lines.Concatenate( "\r\n" ) );
            }
        }

        void EnsureLine( List<string> lines, string line )
        {
            if( !lines.Contains( line ) ) lines.Add( line );
        }

        void RemoveLine( List<string> lines, string line ) => lines.Remove( line );
    }
}
