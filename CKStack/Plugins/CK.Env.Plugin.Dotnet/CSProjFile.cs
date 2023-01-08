using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.MSBuildSln;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.Plugin
{
    public class CSProjFile : GitBranchPluginBase, ICommandMethodsProvider
    {
        private readonly SolutionDriver _solutionDriver;

        public CSProjFile( GitRepository f,
                           NormalizedPath branchPath,
                           SolutionDriver solutionDriver )
             : base( f, branchPath )
        {
            _solutionDriver = solutionDriver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => _solutionDriver.BranchPath.AppendPart( nameof( CSProjFile ) );

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            var solution = _solutionDriver.GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return;

            foreach( var p in solution.Projects.Select( p => p.Tag<MSProject>() ).Where( p => p != null ) )
            {
                if( p!.ProjectFile != null )
                {
                    // Removing the old <Import Project="..\Common\Shared.props" />.
                    foreach( var pFile in p.ProjectFile.AllFiles )
                    {
                        pFile.RemoveImports( i => string.Compare( i.Path.LastPart, "Shared.props", true ) == 0 );
                    }
                }
            }
            solution.Tag<SolutionFile>()!.Save( m );
        }
    }
}
