using CK.Core;
using CK.Setup;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CK.Env.MSBuild
{
    public class DependencyContext
    {
        readonly Solution[] _solutions;
        readonly Dictionary<string, Package> _packages;
        readonly Dictionary<Project, ProjectItem> _projects;
        readonly CKTrait _frameworks;
        ProjectDependencyResult _projectDependencies;

        internal class Package : IDependentPackage, IDependentItem, IDependentItemRef
        {
            // This contains only the Solution of the Project for local projects:
            // The Package (published) requires its Solution.
            // This is null otherwise.
            readonly IDependentItemRef[] _requires;

            internal Package( ProjectItem localProject )
            {
                _requires = new IDependentItemRef[] { localProject.Project.Solution };
                Project = localProject.Project;
                Version = null;
                PackageId = Project.Name;
                FullName = Project.Name;
            }

            internal Package( string packageId, SVersion v, string fullName )
            {
                PackageId = packageId;
                Version = v;
                FullName = fullName;
            }

            /// <summary>
            /// Gets the local project that produces this package.
            /// For such package this <see cref="FullName"/> is the <see cref="ProjectBase.Name"/>
            /// and this <see cref="Version"/> is null.
            /// </summary>
            public Project Project { get; }

            /// <summary>
            /// Gets the package identifier.
            /// </summary>
            public string PackageId { get; }

            /// <summary>
            /// Gets the referenced version.
            /// This is null for a locally published project.
            /// </summary>
            public SVersion Version { get; }

            /// <summary>
            /// Gets <see cref="PackageId"/>/<see cref="Version"/> for external packages
            /// and the versionless package identifier if the project is locally published.
            /// </summary>
            public string FullName { get; }

            public override string ToString() => Project == null
                                                    ? $"External: {FullName}"
                                                    : $"Local: {Project}";

            IDependentItemContainerRef IDependentItem.Container => null;

            IDependentItemRef IDependentItem.Generalization => null;

            IEnumerable<IDependentItemRef> IDependentItem.Requires => _requires;

            IEnumerable<IDependentItemRef> IDependentItem.RequiredBy => null;

            IEnumerable<IDependentItemGroupRef> IDependentItem.Groups => null;

            object IDependentItem.StartDependencySort( IActivityMonitor m ) => this;

            bool IDependentItemRef.Optional => false;

        }

        /// <summary>
        /// This internal class is the IDepentItem that represents a Project
        /// in a SortableSolutionFile graph.
        /// </summary>
        internal class ProjectItem : IDependentProject, IDependentItem, IDependentItemRef
        {
            readonly Project _p;
            readonly List<IDependentItemRef> _requires;

            ProjectItem( Project p, Dictionary<Project, ProjectItem> cache )
            {
                _p = p;
                cache.Add( p, this );
                _requires = p.Deps.Projects.Select( dep => Create( dep.TargetProject, cache ) ).ToList<IDependentItemRef>();
            }

            static public ProjectItem Create( Project p, Dictionary<Project,ProjectItem> cache )
            {
                if( cache.TryGetValue( p, out var item ) ) return item;
                return new ProjectItem( p, cache );
            }

            /// <summary>
            /// Gets the project itself.
            /// </summary>
            public Project Project => _p;

            /// <summary>
            /// Whether this project belongs to the Published ones.
            /// When a project is published, its name is the one of the associated DependencyContext.Package
            /// and is necessarily unique: we can use its file name as the item's FullName (with the .csproj
            /// extension: the naked project name is used for its Package's FullName).
            /// But when a project is not published, we scope its name to its solution name to compute the
            /// FullName so that utility projects can have the same name in different solutions.
            /// </summary>
            public bool IsPublished { get; set; }

            /// <summary>
            /// Gets the full name of this project item (see <see cref="IsPublished"/>).
            /// </summary>
            public string FullName => IsPublished
                                        ? _p.Path.LastPart
                                        : _p.Solution.UniqueSolutionName + '/' + _p.Path.LastPart;

            internal void AddRequires( IDependentItemRef p ) => _requires.Add( p );

            IDependentItemContainerRef IDependentItem.Container => _p.Solution.SpecialType == SolutionSpecialType.IncludedSecondarySolution
                                                            ? _p.Solution.PrimarySolution
                                                            : _p.Solution;

            IDependentItemRef IDependentItem.Generalization => null;

            IEnumerable<IDependentItemRef> IDependentItem.Requires => _requires;

            IEnumerable<IDependentItemRef> IDependentItem.RequiredBy => null;

            IEnumerable<IDependentItemGroupRef> IDependentItem.Groups => null;

            bool IDependentItemRef.Optional => false;

            object IDependentItem.StartDependencySort( IActivityMonitor m ) => _p;
        }

        DependencyContext(
            Solution[] solutions,
            Dictionary<string, Package> packages,
            Dictionary<Project, ProjectItem> projects,
            CKTrait frameworks )
        {
            _solutions = solutions;
            _packages = packages;
            _projects = projects;
            _frameworks = frameworks;
        }

        public ProjectDependencyResult ProjectDependencies
        {
            get
            {
                Package FindPackage( DeclaredPackageDependency  d, bool isIncludedSolutionProject )
                {
                    // Check for a published Package.
                    if( _packages.TryGetValue( d.PackageId, out Package p ) )
                    {
                        // If this published Package is from the primary solution of
                        // the project's solution that has SpecialType=IncludedSecondarySolution, we ignore
                        // the dependency as if the dependency was a ProjectReference instead of a PackageReference.
                        if( isIncludedSolutionProject
                            && p.Project.Solution == d.Owner.Solution.PrimarySolution )
                        {
                             p = null;
                        }
                    }
                    else
                    {
                        // If it is not a published package, then we must find it as an external package.
                        p = _packages[d.PackageId + '/' + d.Version];
                    }
                    return p;
                }

                if( _projectDependencies == null )
                {
                    var allDeps = _projects.Keys.SelectMany( p => p.Deps.Packages )
                                        .Select( d => (Dep: d, Target: FindPackage( d, d.Owner.Solution.SpecialType == SolutionSpecialType.IncludedSecondarySolution )) )
                                        .Where( t => t.Target != null )
                                        .Select( t => new ProjectDependencyResult.ProjectDepencyRow(
                                                        t.Dep,
                                                        _projects[t.Dep.Owner],
                                                        t.Target
                                                         ) )
                                        .ToList();

                    var result = new List<ProjectDependencyResult.FrameworkDependencies>();
                    foreach( var f in _frameworks.AtomicTraits )
                    {
                        var fDeps = allDeps.Where( d => d.RawPackageDependency.Frameworks.Intersect( f ) == f ).ToList();
                        result.Add( new ProjectDependencyResult.FrameworkDependencies( f, fDeps ) );
                    }
                    _projectDependencies = new ProjectDependencyResult( allDeps, result );
                }
                return _projectDependencies;
            }
        }

        public SolutionDependencyResult AnalyzeDependencies( IActivityMonitor m, SolutionSortStrategy content )
        {
            var sortables = new Dictionary<Solution, SortableSolutionFile>();
            // Handles Primary solutions first.
            foreach( var s in _solutions )
            {
                if( s.PrimarySolution != null ) continue;
                sortables.Add( s, new SortableSolutionFile( s, GetProjectItems( s, content ) ) );
            }
            if( content == SolutionSortStrategy.EverythingExceptBuildProjects )
            {
                // Handles Secondary solutions.
                foreach( var s in _solutions )
                {
                    if( s.PrimarySolution == null ) continue;
                    if( s.SpecialType == SolutionSpecialType.IncludedSecondarySolution )
                    {
                        var primary = sortables[s.PrimarySolution];
                        primary.AddSecondaryProjects( GetProjectItems( s, content ) );
                    }
                    else sortables.Add( s, new SortableSolutionFile( s, GetProjectItems( s, content ) ) );
                }
            }

            IDependencySorterResult result = DependencySorter.OrderItems( m, sortables.Values, null );
            if( !result.IsComplete )
            {
                return new SolutionDependencyResult( content, result );
            }
            // Building the list of SolutionDependencyResult.DependencyRow.
            var table = result.SortedItems
                          // 1 - Selects solutions along with their ordered index.
                          .Where( sorted => sorted.GroupForHead == null && sorted.Item is SortableSolutionFile )
                          .Select( ( sorted, idx ) =>
                                        (
                                            Index: idx,
                                            Solution: (Solution)sorted.StartValue,
                                            // The projects from this solution that reference packages that are
                                            // produced by local solutions have a direct requires to a Package
                                            // that has a local Project.
                                            // 2 - First Map: LocalRefs is a set of value tuple (Project Origin, Package Target).
                                            LocalRefs: sorted.GetAllChildren()
                                                            .Select( c => (ProjectSorted: c, Project: c.StartValue as Project) )
                                                            .Where( c => c.Project != null )
                                                            .SelectMany( c => c.ProjectSorted.DirectRequires
                                                                            .Select( r => r.Item )
                                                                            .OfType<Package>()
                                                                            .Where( package => package.Project != null )
                                                                            .Select( package => (Origin: c.Project, Target: package) )
                                        )) )
                           // 3 - Second Map: Expands the LocalRefs.
                           .SelectMany( s => s.LocalRefs.Any()
                                                ? s.LocalRefs.Select( r => new SolutionDependencyResult.DependencyRow
                                                                (
                                                                    s.Index,
                                                                    s.Solution,
                                                                    r.Origin,
                                                                    r.Target.Project
                                                                ) )
                                                : new[] { new SolutionDependencyResult.DependencyRow( s.Index, s.Solution, null, null ) }
                                      )
                            .ToList();
            Debug.Assert( table.Select( r => r.Index ).IsSortedLarge() );

            // Now that the table of SolutionDependencyResult.DependencyRow is built, use it to compute the
            // pure solution dependency graph.
            Solution current = null;
            var depSolutions = new SolutionDependencyResult.DependentSolution[sortables.Count];
            foreach( var r in table )
            {
                if( current != r.Solution )
                {
                    current = r.Solution;
                    depSolutions[r.Index] = new SolutionDependencyResult.DependentSolution( current, r.Index, table, s => depSolutions.First( x => x.Solution == s ) );
                }
            }
            return new SolutionDependencyResult( content, result, ProjectDependencies, table, depSolutions );
        }


        IEnumerable<ProjectItem> GetProjectItems( Solution s, SolutionSortStrategy content )
        {
            switch( content )
            {
                case SolutionSortStrategy.PublishedProjects:
                    return s.PublishedProjects.Select( p => _projects[p] );
                case SolutionSortStrategy.PublishedAndTestsProjects:
                    return s.PublishedProjects.Concat( s.TestProjects ).Distinct().Select( p => _projects[p] );
                default:
                    return s.AllProjects.Except( s.BuildProjects ).Select( p => _projects[p] );
            }
        }

        /// <summary>
        /// Factory method for a <see cref="DependencyContext"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solutionFiles">The set of <see cref="Solution"/> to consider.</param>
        /// <returns>A new depednency context.</returns>
        static public DependencyContext Create( IActivityMonitor m, IEnumerable<Solution> solutionFiles )
        {
            var packages = new Dictionary<string, Package>();
            var solutions = solutionFiles.ToArray();
            var projectItems = new Dictionary<Project, ProjectItem>();
            var frameworks = MSBuildContext.Traits.EmptyTrait;
            using( m.OpenDebug( "Creating all the ProjectItem for all projects in all solutions." ) )
            {
                foreach( var s in solutions )
                {
                    using( m.OpenDebug( $"Solution {s.UniqueSolutionName}." ) )
                    {
                        foreach( var project in s.AllProjects )
                        {
                            frameworks = frameworks.Union( project.TargetFrameworks );
                            ProjectItem.Create( project, projectItems );
                        }
                    }
                }
            }
            using( m.OpenInfo( "Creating Package for all Published projects in all solutions." ) )
            {
                foreach( var s in solutions )
                {
                    using( m.OpenTrace( $"Solution {s.UniqueSolutionName}." ) )
                    {
                        foreach( var p in s.PublishedProjects )
                        {
                            if( packages.TryGetValue( p.Name, out var alreadyPublished ) )
                            {
                                m.Error( $"{p} is already published: {alreadyPublished}." );
                                return null;
                            }
                            ProjectItem projectItem = projectItems[p];
                            projectItem.IsPublished = true;
                            packages.Add( p.Name, new Package( projectItem ) );
                            m.Info( $"Package {p.Name} created." );
                        }
                    }
                }
            }
            // 3 - Create the requirements between each project and either
            //     a Package that is bound to a Published project or to an
            //     external Package.
            //     For projects that belong to a secondary solution whose SpecialType is
            //     IncludedSecondarySolution, we depends on the project instead of the published Package.
            foreach( var project in projectItems.Values )
            {
                foreach( var dep in project.Project.Deps.Packages )
                {
                    IDependentItemRef refTarget;
                    if( packages.TryGetValue( dep.PackageId, out Package target ) )
                    {
                        // Dependency to a Published projects from the primary solution are
                        // transfomed into requirements to the Project itself.
                        refTarget = target;
                        if( project.Project.Solution.SpecialType == SolutionSpecialType.IncludedSecondarySolution
                            && target.Project.Solution == project.Project.Solution.PrimarySolution )
                        {
                            refTarget = projectItems[target.Project];
                        }
                    }
                    else
                    {
                        // Dependency to an external Package.
                        refTarget = RegisterExternal( packages, dep );
                    }
                    project.AddRequires( refTarget );
                }
            }
            return new DependencyContext( solutions, packages, projectItems, frameworks );
        }

        static Package RegisterExternal( Dictionary<string, Package> externals, DeclaredPackageDependency dep )
        {
            string fullName = dep.PackageId + '/' + dep.Version.ToString();
            if( !externals.TryGetValue( fullName, out var p ) )
            {
                p = new Package( dep.PackageId, dep.Version, fullName );
                externals.Add( fullName, p );
            }
            return p;
        }

    }
}
