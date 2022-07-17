using CK.Core;
using CK.Env.MSBuildSln;
using System.Diagnostics;

namespace CK.Env.Plugin
{
    public class GlobalJsonFile : TextFilePluginBase, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;
        readonly SolutionDriver _solutionDriver;

        public GlobalJsonFile( GitRepository f, NormalizedPath branchPath, SolutionSpec solutionSpec, SolutionDriver solutionDriver )
             : base( f, branchPath, branchPath.AppendPart( "global.json" ) )
        {
            _solutionSpec = solutionSpec;
            _solutionDriver = solutionDriver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( nameof( GlobalJsonFile ) );

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor monitor )
        {
            if( !this.CheckCurrentBranch( monitor ) ) return;

            if( _solutionSpec.GlobalJsonSdkVersion != null )
            {
                CreateOrUpdate( monitor, $"{{ \"sdk\": {{ \"version\": \"{_solutionSpec.GlobalJsonSdkVersion}\" }} }}" );
                var solution = _solutionDriver.GetSolution( monitor, allowInvalidSolution: true );
                if( solution != null )
                {
                    solution.Tag<SolutionFile>()?.EnsureSolutionItemFile( monitor, FilePath.RemovePrefix( BranchPath ) );
                }
            }
            else
            {
                Delete( monitor );
                var solution = _solutionDriver.GetSolution( monitor, allowInvalidSolution: true );
                if( solution != null )
                {
                    solution.Tag<SolutionFile>()?.RemoveSolutionItemFile( monitor, FilePath.RemovePrefix( BranchPath ) );
                }
            }
        }
    }
}
