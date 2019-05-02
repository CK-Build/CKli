using CK.Core;
using CK.Setup;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates the result of the dependencies analysis at the solution level.
    /// This is produced by <see cref="DependencyAnalyser.CreateDependencyContext(IActivityMonitor, SolutionSortStrategy)"/>.
    /// </summary>
    public class SolutionDependencyContext
    {
        readonly Dictionary<object, DependentSolution> _index;

        /// <summary>
        /// Error constructor.
        /// </summary>
        /// <param name="c">The content strategy.</param>
        /// <param name="rSolution">The dependency sorter result.</param>
        internal SolutionDependencyContext(
            SolutionSortStrategy c,
            IDependencySorterResult rSolution,
            BuildProjectsInfo buildProjectsInfo )
        {
            Debug.Assert( buildProjectsInfo != null && rSolution != null && !rSolution.IsComplete );
            SolutionSortStrategy = c;
            RawSolutionSorterResult = rSolution;
            BuildProjectsInfo = buildProjectsInfo;
            DependencyTable = Array.Empty<DependentSolution.Row>();
            Solutions = Array.Empty<DependentSolution>();
        }

        internal SolutionDependencyContext(
            Dictionary<object, DependentSolution> index,
            SolutionSortStrategy strategy,
            IDependencySorterResult r,
            IReadOnlyList<DependentSolution.Row> t,
            IReadOnlyList<DependentSolution> solutions,
            BuildProjectsInfo buildProjectsInfo )
        {
            Debug.Assert( r != null && r.IsComplete && t != null && solutions != null );
            _index = index;
            SolutionSortStrategy = strategy;
            RawSolutionSorterResult = r;
            BuildProjectsInfo = buildProjectsInfo;
            DependencyTable = t;
            Solutions = solutions;
            PackageDependencies = t.Where( row => row.Origin != null )
                                   .SelectMany( row => row.GetReferences().Select( pR => new LocalPackageDependency( row, pR, _index ) ) )
                                   .ToArray();
            for( int i = solutions.Count - 1; i >= 0; --i ) solutions[i].Initialize( this );
        }

        /// <summary>
        /// Gets the kind of projects that have been considered to sort solutions.
        /// </summary>
        public SolutionSortStrategy SolutionSortStrategy { get; }

        /// <summary>
        /// Gets the details of the dependencies between solutions.
        /// Solutions that have no dependencies appear once with null <see cref="DependencyRow.Origin"/>
        /// and <see cref="DependencyRow.Target"/>.
        /// The <see cref="PackageDependencies"/> is a more abstract view of this.
        /// </summary>
        public IReadOnlyList<DependentSolution.Row> DependencyTable { get; }

        /// <summary>
        /// Gets the package dependencies between solutions.
        /// This is a somehow like the <see cref="DependencyTable"/> except that <see cref="DependentSolution"/>
        /// are referenced (instead of <see cref="PrimarySolution"/>), that there is no row row for a solution that has
        /// no dependency and that each package reference (<see cref="LocalPackageDependency.Reference"/>) appears instead
        /// of the potential multiple <see cref="DependentSolution.Row.GetReferences()"/>.
        /// </summary>
        public IReadOnlyCollection<LocalPackageDependency> PackageDependencies { get; }

        /// <summary>
        /// Gets the global, sorted, dependencies informations between solutions.
        /// </summary>
        public IReadOnlyList<DependentSolution> Solutions { get; }

        /// <summary>
        /// Gets the <see cref="IDependencySorterResult"/> of the Solution/Project graph.
        /// Never null.
        /// </summary>
        public IDependencySorterResult RawSolutionSorterResult { get; }

        /// <summary>
        /// Gets whether solutions and their projects failed to be successfully ordered
        /// or <see cref="BuildProjectsInfo"/> is on error.
        /// </summary>
        public bool HasError => !RawSolutionSorterResult.IsComplete || BuildProjectsInfo.HasError;

        /// <summary>
        /// Gets the build info. Never null.
        /// </summary>
        public BuildProjectsInfo BuildProjectsInfo { get; }

    }

}
