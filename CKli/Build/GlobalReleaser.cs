using CK.Core;
using CK.Env.MSBuild;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli
{
    public class GlobalReleaser
    {
        readonly Dictionary<Solution, SolutionReleaser> _solutions;

        GlobalReleaser( Dictionary<Solution,SolutionReleaser> solutions )
        {
            _solutions = solutions;
            foreach( var s in _solutions.Values ) s.Initialize( this );
        }

        public SolutionReleaser FindBySolution( Solution s ) => _solutions[s];

        public SolutionReleaser FindBySolution( SolutionDependencyResult.DependentSolution s ) => _solutions[s.Solution];

        public static GlobalReleaser Create(
            IActivityMonitor m,
            XSolutionCentral solutions )
        {
            // Consider all GitFolders that contain at least a solution definition in 'develop-LTS' branch.
            var gitFolders = solutions.AllDevelopSolutions.Select( s => s.GitBranch.Parent.GitFolder )
                                .Distinct()
                                .Select( g => (GitFolder: g, Info: g.GetVersionRepositoryInfo( m )) )
                                .ToList();
            if( gitFolders.Any( g => g.Info == null ) ) return null;
            if( !gitFolders.Any() )
            {
                m.Error( $"Unable to find '{solutions.World.DevelopBranchName}' solutions." );
                return null;
            }
            var deps = DependencyContext.Create( m, solutions.AllDevelopSolutions.Select( x => x.Solution ) );
            if( deps == null ) return null;
            SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
            if( r.HasError )
            {
                r.RawSorterResult.LogError( m );
                return null;
            }
            var solutionsToReleaser = r.Solutions
                                            .Select( s => (S: s, G: gitFolders.First( g => g.GitFolder.SubPath == s.Solution.SolutionFolderPath )) )
                                            .ToDictionary( s => s.S.Solution, s => new SolutionReleaser( s.G.GitFolder, s.S, s.G.Info ) );
            return new GlobalReleaser( solutionsToReleaser );
        }
    }
}
