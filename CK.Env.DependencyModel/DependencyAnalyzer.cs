using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Root class of dependency analysis.
    /// A DependencyAnalyzer is obtained from <see cref="ISolutionContext.GetDependencyAnalyser"/>.
    /// </summary>
    public class DependencyAnalyzer
    {
        readonly IReadOnlyCollection<ISolution> _solutions;
        readonly ISolutionContext _solutionContext;
        readonly Dictionary<Artifact, LocalPackageItem> _packages;
        readonly ProjectItem.Cache _projects;
        readonly IReadOnlyList<PackageReference> _externalRefs;
        readonly int _version;
        readonly SolutionDependencyContext _defaultDependencyContext;


        /// <summary>
        /// Package items are not bound to any container (PrimarySolution). There is 2 kind of packages:
        /// locally produced (the item requires the Solution that owns its source code), and external
        /// packages.
        /// This handles the locally produced package.
        /// </summary>
        class LocalPackageItem : IDependentItem, IDependentItemRef
        {
            // This contains only the Solution of the Project for local projects (the Package -published- requires the
            // Solution that owns its source code.)
            readonly IDependentItemRef[] _requires;

            internal LocalPackageItem( Artifact package, IProject project )
            {
                _requires = new IDependentItemRef[] { (IDependentItemRef)project.Solution };
                Project = project;
                Package = package;
            }

            /// <summary>
            /// Gets the local project that produces this package.
            /// </summary>
            public IProject Project { get; }

            /// <summary>
            /// Gets the package information (name and type).
            /// </summary>
            public Artifact Package { get; }

            /// <summary>
            /// Gets the <see cref="Package"/>'s <see cref="Artifact.TypedName"/>.
            /// </summary>
            public string FullName => Package.TypedName;

            public override string ToString() => Project.ToString();

            IDependentItemContainerRef IDependentItem.Container => null;

            IDependentItemRef IDependentItem.Generalization => null;

            IEnumerable<IDependentItemRef> IDependentItem.Requires => _requires;

            IEnumerable<IDependentItemRef> IDependentItem.RequiredBy => null;

            IEnumerable<IDependentItemGroupRef> IDependentItem.Groups => null;

            object IDependentItem.StartDependencySort( IActivityMonitor m ) => this;

            bool IDependentItemRef.Optional => false;

        }

        /// <summary>
        /// This internal class is the IDependentItem that represents a Project
        /// in a solution graph.
        /// </summary>
        class ProjectItem : IDependentItem, IDependentItemRef
        {
            readonly IProject _p;
            readonly List<IDependentItemRef> _requires;
            readonly Cache _cache;

            public class Cache
            {
                readonly Dictionary<IProject, ProjectItem> _cache;

                public Cache()
                {
                    _cache = new Dictionary<IProject, ProjectItem>();
                }

                public ProjectItem this[IProject p] => _cache[p];

                public IEnumerable<IProject> AllProjects => _cache.Keys;

                public IEnumerable<ProjectItem> AllProjectItems => _cache.Values;

                /// <summary>
                /// Dirty trick: when set to true this property forces all ProjectItem's <see cref="IDependentItem.Container"/>
                /// to be null and <see cref="IDependentItem.Requires"/> to contain the ProjectItem requirements instead of
                /// the <see cref="LocalPackageItem"/> for locally published packages.
                /// We use this to reuse ProjectItems to compute Build projects graph dependencies
                /// (this graph ignores the solutions and focuses only on projects).
                /// </summary>
                public bool PureProjectsMode { get; set; }

                internal void Add( IProject p, ProjectItem pItem ) => _cache.Add( p, pItem );

                public ProjectItem Create( IProject p )
                {
                    if( _cache.TryGetValue( p, out var item ) ) return item;
                    return new ProjectItem( p, this );
                }

            }

            ProjectItem( IProject p, Cache cache )
            {
                _p = p;
                _cache = cache;
                cache.Add( p, this );
                _requires = p.ProjectReferences.Select( dep => cache.Create( dep.Target ) ).ToList<IDependentItemRef>();
            }

            /// <summary>
            /// Gets the project itself.
            /// </summary>
            public IProject Project => _p;

            /// <summary>
            /// Gets the full name of this project item (see <see cref="IsPublished"/>).
            /// </summary>
            public string FullName => _p.Name;

            internal void AddRequires( IDependentItemRef p ) => _requires.Add( p );

            IDependentItemContainerRef IDependentItem.Container => _cache.PureProjectsMode
                                                                    ? null
                                                                    : (IDependentItemContainerRef)_p.Solution;

            IDependentItemRef IDependentItem.Generalization => null;

            IEnumerable<IDependentItemRef> IDependentItem.Requires
            {
                get
                {
                    if( _cache.PureProjectsMode )
                    {
                        return _requires.Select( d => d is LocalPackageItem p && p.Project != null ? _cache[p.Project] : d );
                    }
                    return _requires;
                }
            }

            IEnumerable<IDependentItemRef> IDependentItem.RequiredBy => null;

            IEnumerable<IDependentItemGroupRef> IDependentItem.Groups => null;

            bool IDependentItemRef.Optional => false;

            object IDependentItem.StartDependencySort( IActivityMonitor m ) => _p;

            public override string ToString() => _p.ToString();
        }

        DependencyAnalyzer(
            IActivityMonitor m,
            IReadOnlyCollection<ISolution> solutions,
            ISolutionContext solutionCtx,
            Dictionary<Artifact, LocalPackageItem> packages,
            ProjectItem.Cache projects,
            List<PackageReference> externalRefs,
            bool traceGraphDetails )
        {
            _solutions = solutions;
            _solutionContext = solutionCtx;
            _version = _solutionContext?.Version ?? 0;
            _packages = packages;
            _projects = projects;
            _externalRefs = externalRefs;
            _defaultDependencyContext = CreateDependencyContext( m, traceGraphDetails );
        }

        /// <summary>
        /// Gets the set of solutions.
        /// </summary>
        public IReadOnlyCollection<ISolution> Solutions => _solutions;

        /// <summary>
        /// Gets whether the <see cref="Solutions"/> has changed and this analyzer is no more
        /// up to date: a new one should be obtained from <see cref="ISolutionContext.GetDependencyAnalyser"/>.
        /// </summary>
        public bool IsObsolete => _solutionContext == null ? true : _version != _solutionContext.Version;

        /// <summary>
        /// Gets all the external package references.
        /// </summary>
        public IReadOnlyList<PackageReference> ExternalReferences => _externalRefs;

        /// <summary>
        /// Gets the information about build projects (see <see cref="Solution.BuildProject"/>)
        /// and their dependencies.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="traceGraphDetails">True to trace the details of the input and output (sorted) graphs.</param>
        /// <returns>The build projects information.</returns>
        BuildProjectsInfo GetBuildProjectInfo( IActivityMonitor m, bool traceGraphDetails )
        {
            Debug.Assert( !_projects.PureProjectsMode );
            _projects.PureProjectsMode = true;
            try
            {
                using( m.OpenTrace( $"Creating Build Projects information." ) )
                {
                    IDependencySorterResult rBuildProjects = DependencySorter.OrderItems( m, _projects.AllProjectItems.Where( p => p.Project.IsBuildProject ), null );
                    if( !rBuildProjects.IsComplete )
                    {
                        rBuildProjects.LogError( m );
                        return new BuildProjectsInfo( rBuildProjects, null );
                    }
                    else
                    {
                        var rankedProjects = rBuildProjects.SortedItems
                                                    .Where( i => i.Item is ProjectItem )
                                                    .Select( i => (i.Rank,
                                                                   Project: ((ProjectItem)i.Item).Project,
                                                                   DirectDeps: i.Requires
                                                                                .Select( s => s.Item )
                                                                                .OfType<ProjectItem>()
                                                                                .Select( p => p.Project ),
                                                                   AllDeps: i.GetAllRequires()
                                                                             .Select( s => s.Item )
                                                                             .OfType<ProjectItem>()
                                                                             .Select( p => p.Project )) );

                        var zeroBuildProjects = rankedProjects.Select( ( p, idx ) => new ZeroBuildProjectInfo(
                                        idx,
                                        p.Rank,
                                        p.Project,
                                        // UpgradePackages: Among direct dependencies, consider only the
                                        //                  published projects and the ones who are actually referenced
                                        //                  as a package (ignores ProjectReference).
                                        p.DirectDeps
                                            .Where( d => d.IsPublished
                                                         && p.Project.PackageReferences.Any( r => d.GeneratedArtifacts
                                                                                                   .Select( a => a.Artifact )
                                                                                                   .Contains( r.Target.Artifact ) ) )
                                            .ToArray(),
                                        // UpgradeZeroProjects: Among direct dependencies, consider all the
                                        //                      published projects, the ones who are actually referenced
                                        //                      as a package AND the ones that are ProjectReference.
                                        //                      ProjectReference MUST be transformed into PackageReference
                                        //                      during ZeroBuild.
                                        p.DirectDeps
                                            .Where( d => d.IsPublished )
                                            .ToArray(),

                                        // Dependencies: Considers all the projects. 
                                        p.AllDeps.ToArray()

                                        ) )
                            .ToArray();

                        Debug.Assert( zeroBuildProjects.Select( z => z.Rank ).IsSortedLarge() );
                        Debug.Assert( zeroBuildProjects.Select( z => z.Index ).IsSortedStrict() );

                        return new BuildProjectsInfo( rBuildProjects, zeroBuildProjects );
                    }
                }
            }
            finally
            {
                _projects.PureProjectsMode = false;
            }
        }

        class SolutionItem : IDependentItemContainer
        {
            readonly ISolution _solution;
            IEnumerable<IDependentItemRef> _projects;

            internal SolutionItem( ISolution f, IEnumerable<IDependentItemRef> projects )
            {
                _solution = f;
                _projects = projects;
            }

            public ISolution Solution => _solution;

            string IDependentItem.FullName => _solution.Name;

            IDependentItemContainerRef IDependentItem.Container => null;

            IEnumerable<IDependentItemRef> IDependentItemGroup.Children => _projects;

            IDependentItemRef IDependentItem.Generalization => null;

            IEnumerable<IDependentItemRef> IDependentItem.Requires => null;

            IEnumerable<IDependentItemRef> IDependentItem.RequiredBy => null;

            IEnumerable<IDependentItemGroupRef> IDependentItem.Groups => null;

            object IDependentItem.StartDependencySort( IActivityMonitor m ) => _solution;

        }

        /// <summary>
        /// Gets the default dependency context: the one that considers all projects
        /// except the build projects.
        /// </summary>
        public SolutionDependencyContext DefaultDependencyContext => _defaultDependencyContext;

        /// <summary>
        /// Creates a new <see cref="SolutionDependencyContext"/>, possibly for a different subset of projects
        /// of the <see cref="Solutions"/> than the default set (all projects except build projects).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="traceGraphDetails">True to trace the details of the input and output (sorted) graphs.</param>
        /// <param name="projectFilter">
        /// Optional project filter.
        /// By default all projects are considered except build projects (see <see cref="Solution.BuildProject"/>).
        /// </param>
        /// <returns>The context or null on error.</returns>
        public SolutionDependencyContext CreateDependencyContext( IActivityMonitor m, bool traceGraphDetails, Func<IProject, bool> projectFilter = null )
        {
            if( projectFilter == null )
            {
                projectFilter = p => !p.IsBuildProject;
            }
            var sortables = new Dictionary<ISolution, SolutionItem>();
            foreach( var s in _solutions )
            {
                var sItem = new SolutionItem( s, s.Projects.Where( p => projectFilter( p ) ).Select( p => _projects[p] ) );
                sortables.Add( s, sItem );
            }

            var options = new DependencySorterOptions();
            if( traceGraphDetails )
            {
                options.HookInput = i => i.Trace( m );
                options.HookOutput = i => i.Trace( m );
            }
            IDependencySorterResult result = DependencySorter.OrderItems( m, sortables.Values, null, options );
            if( !result.IsComplete )
            {
                result.LogError( m );
                return new SolutionDependencyContext( this, result, GetBuildProjectInfo( m, traceGraphDetails ) );
            }
            // Building the list of SolutionDependencyContext.DependencyRow.
            var table = result.SortedItems
                          // 1 - Selects solutions along with their ordered index.
                          .Where( sorted => sorted.GroupForHead == null && sorted.Item is SolutionItem )
                          .Select( ( sorted, idx ) =>
                                        (
                                            Index: idx,
                                            Solution: (Solution)sorted.StartValue,
                                            // The projects from this solution that reference packages that are
                                            // produced by local solutions have a direct requires to a Package
                                            // that has a local Project.
                                            // 2 - First Map: LocalRefs is a set of value tuple (Project Origin, Package Target).
                                            LocalRefs: sorted.Children
                                                            .Select( c => (SProject: c, Project: (Project)c.StartValue) )
                                                            .SelectMany( c => c.SProject.DirectRequires
                                                                            .Select( r => r.Item )
                                                                            .OfType<LocalPackageItem>()
                                                                            .Select( package => (Origin: c.Project, Target: package) )
                                        )) )
                           // 3 - Second Map: Expands the LocalRefs.
                           .SelectMany( s => s.LocalRefs.Any()
                                                ? s.LocalRefs.Select( r => new DependentSolution.Row
                                                                (
                                                                    s.Index,
                                                                    s.Solution,
                                                                    r.Origin,
                                                                    r.Target.Project
                                                                ) )
                                                : new[] { new DependentSolution.Row( s.Index, s.Solution, null, null ) }
                                      )
                            .ToList();
            Debug.Assert( table.Select( r => r.Index ).IsSortedLarge() );

            // Now that the table of SolutionDependencyContext.DependencyRow is built, use it to compute the
            // pure solution dependency graph.
            //
            var index = new Dictionary<object, DependentSolution>();
            ISolution current = null;
            var depSolutions = new DependentSolution[sortables.Count];
            foreach( var r in table )
            {
                if( current != r.Solution )
                {
                    current = r.Solution;
                    var newDependent = new DependentSolution( current, r.Index, table, s => index[s] );
                    depSolutions[r.Index] = newDependent;
                    index.Add( current.Name, newDependent );
                    index.Add( current, newDependent );
                }
            }

            depSolutions = depSolutions.OrderBy( p => p.Rank ).ThenBy( p => p.Index ).ToArray();

            return new SolutionDependencyContext( this, index, result, table, depSolutions, GetBuildProjectInfo( m, traceGraphDetails ) );
        }

        public static DependencyAnalyzer Create( IActivityMonitor m, IReadOnlyCollection<ISolution> solutions, bool traceGraphDetails )
        {
            return Create( m, solutions, traceGraphDetails, null );
        }

        internal static DependencyAnalyzer Create( IActivityMonitor m, IReadOnlyCollection<ISolution> solutions, bool traceGraphDetails, ISolutionContext solutionCtx )
        {
            var packages = new Dictionary<Artifact, LocalPackageItem>();
            var projectItems = new ProjectItem.Cache();
            var externalRefs = new List<PackageReference>();

            // Note: Project to project references are translated into Requirements directly
            //       in the ProjectItem constructor.
            //       After having built the ProjectItem, we handle here the Packages (and PackageReferences between
            //       projects).
            using( m.OpenDebug( "Creating all the ProjectItem for all projects in all solutions." ) )
            {
                foreach( var s in solutions )
                {
                    using( m.OpenDebug( $"Solution {s.Name}." ) )
                    {
                        foreach( var project in s.Projects )
                        {
                            projectItems.Create( project );
                        }
                    }
                }
            }
            using( m.OpenDebug( "Creating Package for all installable Artifacts in all solutions." ) )
            {
                foreach( var s in solutions )
                {
                    using( m.OpenDebug( $"Solution {s.Name}." ) )
                    {
                        foreach( var project in s.Projects )
                        {
                            foreach( var package in project.GeneratedArtifacts.Where( a => a.Artifact.Type.IsInstallable ) )
                            {
                                if( packages.TryGetValue( package.Artifact, out var alreadyPublished ) )
                                {
                                    m.Error( $"'{package.Project.Solution+"->"+package}' is already published by {alreadyPublished.Project.Solution+"->"+alreadyPublished.Project}." );
                                    return null;
                                }
                                packages.Add( package.Artifact, new LocalPackageItem( package.Artifact, project ) );
                                m.Debug( $"Package '{package}' created." );
                            }

                        }
                    }
                }
            }

            // 3 - Create the requirements between each project and Packages that are bound to a
            //     Published project (the LocalPackageItem previuosly created).
            //     When PackageReferences references external Packages, we add it to the ExternalRefs.
            foreach( var project in projectItems.AllProjectItems )
            {
                // Consider package references (Project to Project references are handled by ProjectItem constructors).
                foreach( var dep in project.Project.PackageReferences )
                {
                    if( packages.TryGetValue( dep.Target.Artifact, out LocalPackageItem target ) )
                    {
                        if( target.Project.Solution != project.Project.Solution )
                        {
                            project.AddRequires( target );
                        }
                        else
                        {
                            // A project is referencing a Package that is generated by
                            // its own Solution. This can happen (even if it is strange): for instance to test packages
                            // from the solution itself (the more correct way to do this is to use another
                            // Repository/Solution to test the packages since here you always test the "previous"
                            // package version). 
                            //
                            // We transform the package reference into a project reference so that this edge
                            // case does not create cycles.
                            project.AddRequires( projectItems[target.Project] );
                        }
                    }
                    else
                    {
                        // Dependency to an external Package.
                        externalRefs.Add( dep );
                    }
                }
            }
            return new DependencyAnalyzer(
                        m,
                        solutions,
                        solutionCtx,
                        packages,
                        projectItems,
                        externalRefs,
                        traceGraphDetails );
        }

    }
}
