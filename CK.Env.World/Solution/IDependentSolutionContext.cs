using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

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
        IReadOnlyList<ZeroBuildProjectInfo> ZeroBuildProjects { get; }
    }

    public static class DependentSolutionContextExtension
    {
        public static void LogSolutions( this IDependentSolutionContext @this, IActivityMonitor m, IDependentSolution current = null )
        {
            int rank = -1;
            foreach( var s in @this.Solutions )
            {
                if( rank != s.Rank )
                {
                    rank = s.Rank;
                    m.Info( $" -- Rank {rank}" );
                }
                m.Info( $"{(s == current ? '*' : ' ')}   {s.Index} - {s} => {s.MinimalImpacts.Count}/{s.Impacts.Count}/{s.TransitiveImpacts.Count}" );
            }
        }

    }

}
