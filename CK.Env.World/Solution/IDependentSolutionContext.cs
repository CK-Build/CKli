using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Captures high level dependency information that is enough to orchestrate
    /// global operations at the world level.
    /// </summary>
    public interface IDependentSolutionContext
    {
        /// <summary>
        /// Gets the unique branch name from which all solutions have been analyzed.
        /// If solutions were in more than one branch, this is null.
        /// </summary>
        string UniqueBranchName { get; }

        /// <summary>
        /// Gets the global, sorted, dependencies informations between solutions.
        /// Note that <see cref="IDependentSolution.Index"/> is the index in this list.
        /// </summary>
        IReadOnlyList<IDependentSolution> Solutions { get; }

        /// <summary>
        /// Gets the package dependencies between solutions.
        /// </summary>
        IReadOnlyCollection<ILocalPackageDependency> PackageDependencies { get; }

        /// <summary>
        /// Gets the ordered list of <see cref="ZeroBuildProjectInfo"/>.
        /// Null if an error occurred during its computation.
        /// </summary>
        IReadOnlyList<ZeroBuildProjectInfo> BuildProjectsInfo { get; }
    }

    public static class DependentSolutionContextExtension
    {
        public static void LogSolutions(
            this IDependentSolutionContext @this,
            IActivityMonitor m,
            IDependentSolution current = null,
            Func<IDependentSolution,string> solutionLineDetail = null,
            Action<IActivityMonitor,IDependentSolution> solutionDetail = null )
        {
            int rank = -1;
            foreach( var s in @this.Solutions )
            {
                if( rank != s.Rank )
                {
                    rank = s.Rank;
                    m.Info( $" -- Rank {rank}" );
                }
                if( solutionDetail != null )
                {
                    using( m.OpenInfo( $"{(s == current ? '*' : ' ')}   {s.Index} - {s} {solutionLineDetail?.Invoke( s )}" ) )
                    {
                        solutionDetail( m, s );
                    }
                }
                else
                {
                    m.Info( $"{(s == current ? '*' : ' ')}   {s.Index} - {s} {solutionLineDetail?.Invoke( s )}" );
                }
            }
        }

    }

}
