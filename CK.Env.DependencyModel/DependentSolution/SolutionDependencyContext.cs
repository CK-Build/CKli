using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Encapsulates the result of the dependencies analysis at the solution level.
    /// This is produced by <see cref="DependencyAnalyzer.CreateDependencyContext(IActivityMonitor, bool, Func{IProject, bool})"/>.
    /// This object doesn't hold the potential project filter that has been used to built it.
    /// When <see cref="IsObsolete"/> is true, it must be fully rebuilt from a new <see cref="DependencyAnalyzer"/>.
    /// </summary>
    public class SolutionDependencyContext
    {
        readonly DependencyAnalyzer _analyzer;
        readonly Dictionary<object, DependentSolution> _index;

        /// <summary>
        /// Error constructor.
        /// </summary>
        /// <param name="analyzer">The analyzer.</param>
        /// <param name="rSolution">The dependency sorter result.</param>
        /// <param name="buildProjectsInfo">Build info may be on error or not.</param>
        internal SolutionDependencyContext(
            DependencyAnalyzer analyzer,
            IDependencySorterResult rSolution,
            BuildProjectsInfo buildProjectsInfo )
        {
            Debug.Assert( analyzer != null && buildProjectsInfo != null && rSolution != null && !rSolution.IsComplete );
            _analyzer = analyzer;
            RawSolutionSorterResult = rSolution;
            BuildProjectsInfo = buildProjectsInfo;
            DependencyTable = Array.Empty<DependentSolution.Row>();
            Solutions = Array.Empty<DependentSolution>();
        }

        internal SolutionDependencyContext(
            DependencyAnalyzer analyzer,
            Dictionary<object, DependentSolution> index,
            IDependencySorterResult r,
            IReadOnlyList<DependentSolution.Row> t,
            IReadOnlyList<DependentSolution> solutions,
            BuildProjectsInfo buildProjectsInfo )
        {
            Debug.Assert( analyzer != null && r != null && r.IsComplete && t != null && solutions != null );
            _analyzer = analyzer;
            _index = index;
            RawSolutionSorterResult = r;
            BuildProjectsInfo = buildProjectsInfo;
            DependencyTable = t;
            Solutions = solutions;
            PackageDependencies = t.Where( row => row.Target != null )
                                   .SelectMany( row => row.GetReferences().Select( pR => new LocalPackageDependency( row, pR, _index ) ) )
                                   .ToArray();
            for( int i = solutions.Count - 1; i >= 0; --i ) solutions[i].Initialize( this );
        }

        /// <summary>
        /// Gets the dependency analyzer that built this dependency context.
        /// </summary>
        public DependencyAnalyzer Analyzer => _analyzer;

        /// <summary>
        /// Gets whether the solutions or projects have changed and that a fresh dependency
        /// context should be obtained from an up to date <see cref="Analyzer"/>.
        /// </summary>
        public bool IsObsolete => _analyzer.IsObsolete;

        /// <summary>
        /// Gets the details of the dependencies between solutions.
        /// Solutions that have no dependencies appear once with null <see cref="DependencyRow.Origin"/>
        /// and <see cref="DependencyRow.Target"/>.
        /// The <see cref="PackageDependencies"/> is a more abstract view of this.
        /// </summary>
        public IReadOnlyList<DependentSolution.Row> DependencyTable { get; }

        /// <summary>
        /// Gets the package dependencies between solutions.
        /// This is somehow like the <see cref="DependencyTable"/> except that <see cref="DependentSolution"/>
        /// are referenced (instead of <see cref="Solution"/>), that there is no row for a solution that has
        /// no dependency and that each package reference (<see cref="LocalPackageDependency.Reference"/>) appears instead
        /// of the potential multiple <see cref="DependentSolution.Row.GetReferences()"/>.
        /// </summary>
        public IReadOnlyCollection<LocalPackageDependency> PackageDependencies { get; }

        /// <summary>
        /// Gets the global, sorted, dependencies informations between solutions.
        /// </summary>
        public IReadOnlyList<DependentSolution> Solutions { get; }

        /// <summary>
        /// Gets the dependent solution by its name.
        /// </summary>
        /// <param name="name">The solution name.</param>
        /// <returns>The dependent solution or null.</returns>
        public DependentSolution this[string name] => _index.GetValueWithDefault( name, null );

        /// <summary>
        /// Gets the dependent solution associated to a <see cref="Solution"/>.
        /// </summary>
        /// <param name="s">The solution.</param>
        /// <returns>The dependent solution or null.</returns>
        public DependentSolution this[ISolution s] => _index.GetValueWithDefault( s, null );

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

        /// <summary>
        /// Dumps the <see cref="Solutions"/> order by their index along with optional details.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="current">The current solution (a star will precede its name).</param>
        /// <param name="solutionLineDetail">Optional details to be appended on the information, header, line.</param>
        /// <param name="solutionDetail">
        /// Optional detailed log generator.
        /// When not null, a group is opened by solution and this is called.
        /// </param>
        public void LogSolutions(
            IActivityMonitor m,
            DependentSolution current = null,
            Func<DependentSolution, string> solutionLineDetail = null,
            Action<IActivityMonitor, DependentSolution> solutionDetail = null )
        {
            int rank = -1;
            foreach( var s in Solutions )
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
