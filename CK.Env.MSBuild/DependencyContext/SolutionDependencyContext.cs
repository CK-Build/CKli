using CK.Core;
using CK.Setup;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Encapsulates the result of the dependencies analysis at the solution level.
    /// This is produced by <see cref="DependencyAnalyser.CreateDependencyContext(Core.IActivityMonitor, SolutionSortStrategy)"/>.
    /// </summary>
    public class SolutionDependencyContext : IDependentSolutionContext
    {
        readonly Dictionary<string, DependentSolution> _indexByName;

        /// <summary>
        /// Expanded data that captures for each solution, all referenced projects (<see cref="Target"/>) to
        /// other solutions from its own projects (<see cref="Origin)"/>.
        /// Note that this relation does not support per framework dependencies: a locally produced package can not
        /// have different versions at the same time in a World. An <see cref="InvalidOperationException"/> is thrown
        /// if more than one version exist in <see cref="Origin"/> dependencies that reference the <see cref="Target"/>.
        /// </summary>
        public class DependencyRow
        {
            /// <summary>
            /// Gets the build order for the <see cref="Solution"/>.
            /// </summary>
            public int Index { get; }

            /// <summary>
            /// Gets the solution of the <see cref="Origin"/> project.
            /// </summary>
            public Solution Solution { get; }

            /// <summary>
            /// Gets the project that references the package produced by the <see cref="Target"/> project.
            /// Null if <see cref="Solution"/> does not require any other solution.
            /// </summary>
            public Project Origin { get; }

            /// <summary>
            /// Gets the targeted project by <see cref="Origin"/>.
            /// Null if <see cref="Solution"/> does not require any other solution.
            /// </summary>
            public Project Target { get; }

            /// <summary>
            /// Gets the single version refrence from <see cref="Origin"/> to <see cref="Target"/>.
            /// Null if <see cref="Solution"/> does not require any other solution.
            /// Note that this relation does not support per framework dependencies: a locally produced package can not
            /// have different versions at the same time in a World. An <see cref="InvalidOperationException"/> is thrown
            /// if more than one version exist in <see cref="Origin"/> dependencies that reference the <see cref="Target"/>.
            /// </summary>
            public SVersion Version { get; }

            internal DependencyRow( int idx, Solution s, Project o, Project t )
            {
                Debug.Assert( (o == null) == (t == null) );
                Index = idx;
                Solution = s;
                Origin = o;
                Target = t;
                if( o != null )
                {
                    Version = Origin.Deps.Packages.Single( d => d.PackageId == Target.Name ).Version;
                }
            }

            public override string ToString()
            {
                return $"{Index}|{Solution.UniqueSolutionName}|{Origin?.Name}|{Target?.Name}";
            }
        }

        class LocalDep : ILocalPackageDependency
        {
            readonly DependencyRow _row;

            public LocalDep( DependencyRow row, Dictionary<string, DependentSolution> indexByName )
            {
                _row = row;
                Origin = indexByName[row.Origin.PrimarySolution.UniqueSolutionName];
                Target = indexByName[row.Target.PrimarySolution.UniqueSolutionName];
            }

            public IDependentSolution Origin { get; }

            public string OriginSecondarySolutionName => _row.Origin.Solution.PrimarySolution == null ? null : _row.Origin.Solution.UniqueSolutionName;

            public string OriginProjectName => _row.Origin.Name;

            public SVersion Version => _row.Version;

            public IDependentSolution Target { get; }

            public string TargetSecondarySolutionName => _row.Target.Solution.PrimarySolution == null ? null : _row.Target.Solution.UniqueSolutionName;

            public string TargetProjectName => _row.Target.Name;
        }

        /// <summary>
        /// Simple model for a dependent solution with its direct, minimal and transitive requirements and impacts.
        /// </summary>
        public class DependentSolution : IDependentSolution
        {
            internal DependentSolution(
                Solution s,
                int index,
                ProjectDependencyResult projectDependencies,
                IReadOnlyList<DependencyRow> dependencyTable,
                Func<Solution,DependentSolution> others )
            {
                Solution = s;
                Index = index;
                Requirements = dependencyTable.Where( r => r.Solution == s && r.Target != null )
                                .Select( r => r.Target.Solution )
                                .Distinct()
                                .Select( others )
                                .ToList();

                MinimalRequirements = Requirements.Except( Requirements.SelectMany( r => r.MinimalRequirements ) ).ToList();
                Rank = MinimalRequirements.Count == 0 ? 0 : MinimalRequirements.Max( r => r.Rank ) + 1;
                TransitiveRequirements = Requirements.SelectMany( r => r.TransitiveRequirements ).Distinct().ToList();

                PublishedRequirements = projectDependencies.DependencyTable
                            .Where( r => r.SourceProject.IsPublished
                                        && r.SourceProject.Project.PrimarySolution == Solution
                                        && r.TargetPackage.Project != null )
                            .Select( r => others( r.TargetPackage.Project.PrimarySolution ) )
                            .ToList();

                Debug.Assert( PublishedRequirements.All( d => Requirements.Contains( d ) ) );
            }

            /// <summary>
            /// Gets the solution name. This should be unique across any possible world.
            /// </summary>
            public string UniqueSolutionName => Solution.UniqueSolutionName;

            /// <summary>
            /// Gets the rank of this solution.
            /// </summary>
            public int Rank { get; }

            /// <summary>
            /// Gets the index of this solution.
            /// </summary>
            public int Index { get; }

            /// <summary>
            /// Gets the solution.
            /// </summary>
            public Solution Solution { get; }

            /// <summary>
            /// Gets the direct required solutions: this corresponds to the solutions that generate a package
            /// used in this <see cref="Solution"/>.
            /// </summary>
            public IReadOnlyList<DependentSolution> Requirements { get; }

            /// <summary>
            /// Gets the direct required solutions for the published packages of this solution:
            /// this corresponds to the solutions that generate a package used by a published package in
            /// this <see cref="Solution"/>.
            /// </summary>
            public IReadOnlyList<DependentSolution> PublishedRequirements { get; }

            /// <summary>
            /// Gets the minimal set of required solutions: this is the sub set of the <see cref="Requirements"/>
            /// from which solutions that depend from other required solutions are removed.
            /// </summary>
            public IReadOnlyList<DependentSolution> MinimalRequirements { get; }

            /// <summary>
            /// Gets the maximal set of required solutions (the transitive closure of this <see cref="Requirements"/>).
            /// </summary>
            public IReadOnlyList<DependentSolution> TransitiveRequirements { get; }

            /// <summary>
            /// Gets the direct impacts of this solution: these are all the solutions that use at least one package
            /// generated by this <see cref="Solution"/>.
            /// </summary>
            public IReadOnlyList<DependentSolution> Impacts { get; private set; }

            /// <summary>
            /// Gets the minimal set of impacted solutions: this is the sub set of the <see cref="Impacts"/>
            /// from which solutions that impact any other impacted solutions are removed.
            /// </summary>
            public IReadOnlyList<DependentSolution> MinimalImpacts { get; private set; }

            /// <summary>
            /// Gets the maximal set of impacted solutions (the transitive closure of this <see cref="Impacts"/>).
            /// </summary>
            public IReadOnlyCollection<DependentSolution> TransitiveImpacts { get; private set; }

            /// <summary>
            /// Gets the locally produced packages that are consumed by this solution.
            /// Only packages that are produced by <see cref="Requirements"/> are considered.
            /// </summary>
            public IReadOnlyCollection<ImportedLocalPackage> ImportedPackages { get; private set; }

            /// <summary>
            /// Gets the packages produced by this solution that are consumed by other solutions.
            /// </summary>
            public IReadOnlyCollection<ExportedLocalPackage> ExportedPackages { get; private set; }

            /// <summary>
            /// Gets the global <see cref="SolutionDependencyContext"/> to which this <see cref="DependentSolution"/> belongs.
            /// </summary>
            public SolutionDependencyContext GlobalResult { get; private set; }

            #region IDependentSolution explicit implementation.

            IGitRepository IDependentSolution.GitRepository => Solution.GitFolder;

            string IDependentSolution.BranchName => Solution.BranchName;

            IReadOnlyList <IDependentSolution> IDependentSolution.Requirements => Requirements;

            IReadOnlyList<IDependentSolution> IDependentSolution.PublishedRequirements => PublishedRequirements;

            IReadOnlyList<IDependentSolution> IDependentSolution.MinimalRequirements => MinimalRequirements;

            IReadOnlyList<IDependentSolution> IDependentSolution.TransitiveRequirements => TransitiveRequirements;

            IReadOnlyList<IDependentSolution> IDependentSolution.Impacts => Impacts;

            IReadOnlyList<IDependentSolution> IDependentSolution.MinimalImpacts => MinimalImpacts;

            IReadOnlyCollection<IDependentSolution> IDependentSolution.TransitiveImpacts => TransitiveImpacts;
            #endregion

            internal void Initialize( SolutionDependencyContext global )
            {
                GlobalResult = global;
                Impacts = global.DependencyTable.Where( r => r.Target != null && r.Target.PrimarySolution == Solution )
                                                .Select( r => r.Solution )
                                                .Distinct()
                                                .Select( i => global.Solutions.First( d => d.Solution == i ) )
                                                .ToList();
                MinimalImpacts = Impacts.Except( Impacts.SelectMany( r => r.TransitiveImpacts ) ).ToList();
                var transitive = new HashSet<DependentSolution>( Impacts );
                foreach( var i in Impacts.SelectMany( r => r.TransitiveImpacts ) ) transitive.Add( i );
                TransitiveImpacts = transitive;

                ImportedPackages = global.PackageDependencies
                                         .Where( d => d.Origin == this )
                                         .Select( d => new ImportedLocalPackage( d ) )
                                         .ToArray();

                ExportedPackages = global.PackageDependencies
                                         .Where( d => d.Target == this )
                                         .Select( d => new ExportedLocalPackage( d ) )
                                         .ToArray();
            }

            /// <summary>
            /// Updates all projects (except Build projects by default or only Build projects) with versions that are
            /// given by <paramref name="localPackageFinder"/> and saves the solution and its updated projects.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <param name="localPackageFinder">The local package version resolver.</param>
            /// <param name="allowDowngrade">Optional allow downgrade. Necessary when cancelling a release.</param>
            /// <param name="allowMissing">Defaults to true to be able to start with empty local feeds.</param>
            /// <param name="buildProjects">Whether build projects (and not normal ones) must be considered.</param>
            /// <returns>Whether the upgrade succeeded.</returns>
            public bool UpdatePackageDependencies(
                IActivityMonitor m,
                Func<IActivityMonitor,string,SVersion> localPackageFinder,
                bool allowDowngrade = false,
                bool allowMissing = true,
                bool buildProjects = false )
            {
                var all = GlobalResult.ProjectDependencies.DependencyTable
                                    .Where( d => d.SourceProject.Project.PrimarySolution == Solution
                                                 && !d.IsExternalDependency
                                                 && d.SourceProject.Project.IsBuildProject == buildProjects )
                                    .Select( d => (
                                            Row: d,
                                            LocalVersion: localPackageFinder( m, d.PackageId )) )
                                    .ToList();

                var changes = all.Where( d => d.LocalVersion == null || d.LocalVersion != d.Row.RawPackageDependency.Version );
                var missings = changes.Where( d => d.LocalVersion == null );
                if( missings.Any() )
                {
                    m.Log( allowMissing ? LogLevel.Warn : LogLevel.Fatal, $"Packages not found locally: {missings.Select( u => u.Row.PackageId ).Concatenate()}." );
                    if( !allowMissing ) return false;
                }
                var downgrade = changes.Where( d => d.LocalVersion != null && d.LocalVersion < d.Row.RawPackageDependency.Version );
                if( downgrade.Any() )
                {
                    foreach( var d in downgrade )
                    {
                        m.Log( allowDowngrade ? LogLevel.Warn : LogLevel.Fatal, $"Local package {d.Row.PackageId} found locally in version {d.LocalVersion} but current reference has a greater version {d.Row.RawPackageDependency.Version}." );
                    }
                    if( !allowDowngrade ) return false;
                }
                var availableChanges = changes.Where( c => c.LocalVersion != null );
                using( m.OpenInfo( $"Upgrading {availableChanges.GroupBy( u => u.Row.PackageId ).Count()} locally available packages." ) )
                {
                    foreach( var u in availableChanges )
                    {
                        u.Row.SourceProject.Project.SetPackageReferenceVersion( m, u.Row.SourceProject.Project.TargetFrameworks, u.Row.PackageId, u.LocalVersion );
                    }
                    return Solution.Save( m );
                }
            }

            /// <summary>
            /// Applies an operation on dependencies before applying it to this <see cref="DependentSolution"/>.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <param name="action">The action to apply.</param>
            /// <returns>True on success, false on error.</returns>
            public bool PullImpacts( IActivityMonitor m, Func<IActivityMonitor, DependentSolution, bool> action )
            {
                return PullImpacts( new HashSet<DependentSolution>(), m, action );
            }

            bool PullImpacts( HashSet<DependentSolution> done, IActivityMonitor m, Func<IActivityMonitor, DependentSolution, bool> action )
            {
                foreach( var d in MinimalRequirements )
                {
                    if( done.Add( d ) ) if( !PullImpacts( done, m, action ) ) return false;
                }
                return action( m, this );
            }

            /// <summary>
            /// Applies an operation to this solution and then to all its <see cref="Impacts"/> recursively.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <param name="action">The action to apply.</param>
            /// <returns>True on success, false on error.</returns>
            public bool PushImpacts( IActivityMonitor m, Func<IActivityMonitor, DependentSolution, bool> action )
            {
                return PushImpacts( new HashSet<DependentSolution>(), m, action );
            }

            bool PushImpacts( HashSet<DependentSolution> done, IActivityMonitor m, Func<IActivityMonitor, DependentSolution, bool> action )
            {
                if( !action( m, this ) ) return false;
                foreach( var d in MinimalImpacts )
                {
                    if( done.Add( d ) ) if( !PushImpacts( done, m, action ) ) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Error constructor.
        /// </summary>
        /// <param name="c">The content strategy.</param>
        /// <param name="rSolution">The dependency sorter result.</param>
        internal SolutionDependencyContext(
            SolutionSortStrategy c,
            IDependencySorterResult rSolution,
            ProjectDependencyResult projectDeps,
            BuildProjectsInfo buildProjectsInfo )
        {
            Debug.Assert( projectDeps != null );
            Debug.Assert( buildProjectsInfo != null && rSolution != null && !rSolution.IsComplete );
            Content = c;
            RawSolutionSorterResult = rSolution;
            BuildProjectsInfo = buildProjectsInfo;
            ProjectDependencies = projectDeps;
            DependencyTable = Array.Empty<DependencyRow>();
            Solutions = Array.Empty<DependentSolution>();
        }

        internal SolutionDependencyContext(
            Dictionary<string, DependentSolution> indexByName,
            SolutionSortStrategy c,
            IDependencySorterResult r,
            ProjectDependencyResult projectDeps,
            IReadOnlyList<DependencyRow> t,
            IReadOnlyList<DependentSolution> solutions,
            BuildProjectsInfo buildProjectsInfo )
        {
            Debug.Assert( r != null && r.IsComplete && t != null && solutions != null );
            _indexByName = indexByName;
            Content = c;
            RawSolutionSorterResult = r;
            ProjectDependencies = projectDeps;
            BuildProjectsInfo = buildProjectsInfo;
            DependencyTable = t;
            Solutions = solutions;
            PackageDependencies = t.Where( row => row.Origin != null )
                                   .Select( row => new LocalDep( row, _indexByName ) )
                                   .ToArray();
            for( int i = solutions.Count - 1; i >= 0; --i ) solutions[i].Initialize( this );
        }

        /// <summary>
        /// Gets the kind of projects that have been considered to sort solutions.
        /// </summary>
        public SolutionSortStrategy Content { get; }

        /// <summary>
        /// Gets the project dependency result.
        /// Never null.
        /// </summary>
        public ProjectDependencyResult ProjectDependencies { get; }

        /// <summary>
        /// Gets the details of the dependencies between solutions.
        /// Solutions that have no dependencies appear once with null <see cref="DependencyRow.Origin"/>
        /// and <see cref="DependencyRow.Target"/>.
        /// The <see cref="PackageDependencies"/> is a more abstract view of this.
        /// </summary>
        public IReadOnlyList<DependencyRow> DependencyTable { get; }

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
        /// Gets the build info.
        /// Never null.
        /// </summary>
        public BuildProjectsInfo BuildProjectsInfo { get; }

        IReadOnlyList<IDependentSolution> IDependentSolutionContext.Solutions => Solutions;

        /// <summary>
        /// Gets the package dependencies between solutions.
        /// This is a more abstract view of the <see cref="DependencyTable"/> that does not
        /// contain any row for a solution that has no dependency.
        /// </summary>
        public IReadOnlyCollection<ILocalPackageDependency> PackageDependencies { get; }


    }

}
