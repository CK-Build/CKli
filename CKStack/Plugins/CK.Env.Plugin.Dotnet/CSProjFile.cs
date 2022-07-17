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

            var csprojs = solution.Projects.Select<IProject, (IProject project, MSProject msproject)>( p => (p, p.Tag<MSProject>()!) ).Where( p => p.Item2 != null );

            foreach( (IProject project, MSProject msproject) in csprojs )
            {
                if( msproject.ProjectFile == null )
                {
                    m.Warn( $"Project {project.Name} has no project file. It is skipped." );
                    continue;
                }
                // Removing the old <Import Project="..\Common\Shared.props" />.
                foreach( var pFile in  msproject.ProjectFile.AllFiles )
                {
                    pFile.RemoveImports( i => string.Compare( i.Path.LastPart, "Shared.props", true ) == 0 );
                }
                if( project.IsPublished )
                {
                    // For test projects we want the IsPackable element to be explicit.
                    msproject.SetIsPackable( m, project.IsTestProject ? true : null );
                }
                else
                {
                    msproject.SetIsPackable( m, false );
                }
                // Saves the project.
                msproject.Save( m );
            }
        }
    }
}
