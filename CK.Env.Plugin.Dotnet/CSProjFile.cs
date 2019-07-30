using System.Linq;
using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.MSBuildSln;
using CK.Text;

namespace CK.Env.Plugin
{
    public class CSProjFile : GitBranchPluginBase, ICommandMethodsProvider
    {
        private readonly SolutionSpec _solutionSpec;
        private readonly SolutionDriver _solutionDriver;

        public CSProjFile(
             GitFolder f,
             NormalizedPath branchPath,
            SolutionSpec solutionSpec,
            SolutionDriver solutionDriver )
             : base( f, branchPath )
        {
            _solutionSpec = solutionSpec;
            _solutionDriver = solutionDriver;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => _solutionDriver.BranchPath.AppendPart( nameof( CSProjFile ) );

        [CommandMethod]
        public bool ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return false;

            var solution = _solutionDriver.GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return false;

            var csprojs = solution.Projects.Select<IProject, (IProject project, MSProject msproject)>( p => (p, p.Tag<MSProject>()) ).Where( p => p.Item2 != null );

            foreach( (IProject project, MSProject msproject) in csprojs )
            {
                if( project.IsPublished )
                {
                    // For test projects we want the IsPackable element to be explicit.
                    msproject.SetIsPackable( m, project.IsTestProject ? true : (bool?)null );
                }
                else
                {
                    msproject.SetIsPackable( m, false );
                }
                // Saves the project.
                if( !msproject.Save( m ) ) return false;
            }
            return true;
        }
    }
}
