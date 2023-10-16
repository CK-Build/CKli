using CK.Core;
using CK.Build;
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
    public sealed class DependencyAnalyzer
    {
        readonly ISolutionContext _solutionContext;
        readonly ProjectItem.Cache _projects;
        readonly List<(ISolution Solution, LocalPackageItem LocalPackage)> _solutionDependencies;
        readonly IReadOnlyList<PackageReference> _externalRefs;
        readonly int _updateSerialNumber;
        readonly SolutionDependencyContext _defaultDependencyContext;

        /// <summary>
        /// Package items are not bound to any container (Solution). There is 2 kind of packages:
        /// locally produced (the item requires the Solution that owns its source code), and external
        /// packages.
        /// This handles the locally produced package.
        /// </summary>
        sealed class LocalPackageItem : IDependentItem, IDependentItemRef
        {
            // This contains only the Solution of the Project for local projects (the Package -published- requires the
            // Solution that owns its source code.)
            readonly IDependentItemRef[] _requires;

            internal LocalPackageItem( Artifact package, IProject project )
            {
                Debug.Assert( project.Solution != null );
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
        sealed class ProjectItem : IDependentItem, IDependentItemRef
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

            // In normal mode, the Solution is its own reference (it implements its own FullName that is enough
            // to reference the container.
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

        DependencyAnalyzer( IActivityMonitor m,
                            ISolutionContext solutionCtx,
                            List<(ISolution, LocalPackageItem)> solutionDependencies,
                            ProjectItem.Cache projects,
                            List<PackageReference> externalRefs,
                            bool traceGraphDetails )
        {
            _solutionContext = solutionCtx;
            _solutionDependencies = solutionDependencies;
            _updateSerialNumber = _solutionContext?.UpdateSerialNumber ?? 0;
            _projects = projects;
            _externalRefs = externalRefs;
            _defaultDependencyContext = CreateDependencyContext( m, traceGraphDetails );
        }

        /// <summary>
        /// Gets the solution context.
        /// </summary>
        public ISolutionContext Solutions => _solutionContext;

        /// <summary>
        /// Gets whether the <see cref="Solutions"/> has changed and this analyzer is no more
        /// up to date: a new one should be obtained from <see cref="ISolutionContext.GetDependencyAnalyser"/>.
        /// </summary>
        public bool IsObsolete => _updateSerialNumber != _solutionContext.UpdateSerialNumber;

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
        BuildProjectsInfo GetBuildProjectInfo( IActivityMonitor m )
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
                                                    .Select( i => (i.Rank, ((ProjectItem)i.Item).Project,
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

        sealed class SolutionItem : IDependentItemContainer
        {
            readonly ISolution _solution;
            readonly IEnumerable<IDependentItemRef> _projects;
            readonly IEnumerable<LocalPackageItem> _requires;

            internal SolutionItem( ISolution f, IEnumerable<IDependentItemRef> projects, IEnumerable<LocalPackageItem> requires )
            {
                _solution = f;
                _projects = projects;
                _requires = requires;
            }

            public ISolution Solution => _solution;

            string IDependentItem.FullName => _solution.Name;

            IDependentItemContainerRef IDependentItem.Container => null;

            IEnumerable<IDependentItemRef> IDependentItemGroup.Children => _projects;

            IDependentItemRef IDependentItem.Generalization => null;

            IEnumerable<IDependentItemRef> IDependentItem.Requires => _requires;

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
        public SolutionDependencyContext CreateDependencyContext( IActivityMonitor m, bool traceGraphDetails, Func<IProject, bool>? projectFilter = null )
        {
            if( projectFilter == null )
            {
                projectFilter = p => !p.IsBuildProject;
            }
            var sortables = new Dictionary<ISolution, SolutionItem>();
            foreach( var s in _solutionContext )
            {
                // The Solution contains its Projects.
                var children = s.Projects.Where( p => projectFilter( p ) ).Select( p => _projects[p] );
                var sItem = new SolutionItem( s, children, _solutionDependencies.Where( deps => deps.Solution == s ).Select( deps => deps.LocalPackage ) );
                sortables.Add( s, sItem );
            }

            var options = new DependencySorterOptions();
            if( traceGraphDetails )
            {
                options.HookInput = i => i.Debug( m );
                options.HookOutput = i => i.Debug( m );
            }
            IDependencySorterResult result = DependencySorter.OrderItems( m, sortables.Values, null, options );
            if( !result.IsComplete )
            {
                result.LogError( m );
                return new SolutionDependencyContext( this, result, GetBuildProjectInfo( m ) );
            }
            // Building the list of DependentSolution.Row.
            var table = result.SortedItems
                          // 1 - Selects solutions along with their ordered index.
                          .Where( sorted => sorted.GroupForHead == null && sorted.Item is SolutionItem )
                          .Select( sorted =>
                                        (
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
                                                                            .Select( package => (Origin: (IPackageReferrer)c.Project, Target: package) ) )
                                                        .Concat( sorted.DirectRequires.Select( r => (Origin:(IPackageReferrer)sorted.StartValue, Target:(LocalPackageItem)r.Item ) ))
                                        ))
                           // 3 - Second Map: Expands the LocalRefs.
                           .SelectMany( s => s.LocalRefs.Any()
                                                ? s.LocalRefs.Select( r => new DependentSolution.Row( r.Origin, r.Target.Project ) )
                                                : new[] { new DependentSolution.Row( s.Solution, null ) }
                                      )
                            .ToList();

            // Now that the table of SolutionDependencyContext.DependencyRow is built, use it to compute the
            // pure solution dependency graph.
            //
            var index = new Dictionary<object, DependentSolution>();
            ISolution current = null;
            var depSolutions = new DependentSolution[sortables.Count];

            int idx = 0;
            foreach( var r in table )
            {
                if( current != r.Origin.Solution )
                {
                    current = r.Origin.Solution;
                    var newDependent = new DependentSolution( current, table, s => index[s] );
                    depSolutions[idx++] = newDependent;
                    index.Add( current.Name, newDependent );
                    index.Add( current, newDependent );
                }
            }
            Array.Sort( depSolutions, ( d1, d2 ) =>
            {
                int cmp = d1.Rank - d2.Rank;
                if( cmp == 0 ) cmp = d1.Solution.Name.CompareTo( d2.Solution.Name );
                return cmp;
            } );
            for( int i = 0; i < depSolutions.Length; ++i ) depSolutions[i].Index = i;

            return new SolutionDependencyContext( this, index, result, table, depSolutions, GetBuildProjectInfo( m ) );
        }

        internal static DependencyAnalyzer? Create( IActivityMonitor m, bool traceGraphDetails, ISolutionContext solutionCtx )
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
                foreach( var s in solutionCtx )
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
                foreach( var s in solutionCtx )
                {
                    using( m.OpenDebug( $"Solution {s.Name}." ) )
                    {
                        foreach( var project in s.Projects )
                        {
                            foreach( var package in project.GeneratedArtifacts.Where( a => a.Artifact.IsValid && a.Artifact.Type.IsInstallable ) )
                            {
                                if( packages.TryGetValue( package.Artifact, out var alreadyPublished ) )
                                {
                                    m.Error( $"'{package.Project.Solution + "->" + package}' is already published by {alreadyPublished.Project.Solution + "->" + alreadyPublished.Project}." );
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
            //     Published project (the LocalPackageItem previously created).
            //     When PackageReferences references external Packages, we add it to the ExternalRefs.
            foreach( var project in projectItems.AllProjectItems )
            {
                // Consider package references (Project to Project references are handled by ProjectItem constructors).
                foreach( var dep in project.Project.PackageReferences )
                {
                    Debug.Assert( project.Project == dep.Owner );
                    if( packages.TryGetValue( dep.Target.Artifact, out LocalPackageItem? target ) )
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
                            m.Warn( $"Project '{dep.Owner.Name}' references the package '{dep.Target.Artifact}' from the same Solution. We consider it as a project reference to '{target.Project.Name}'." );
                            project.AddRequires( projectItems[target.Project] );
                        }
                    }
                    else
                    {
                        // Dependency to an external Package.
                        externalRefs.Add( new PackageReference( dep.Owner, dep.Target ) );
                    }
                }
            }

            var solutionDependencies = new List<(ISolution, LocalPackageItem)>();
            // 4 - Consider the Solution references (like local dotnet tools) that are NOT locally 
            //     produced to add them to the external references list.
            //     When the Solution reference is produced locally, it will have to be mapped as
            //     a required setup item during DependencyContext creation.
            foreach( var p in solutionCtx.SelectMany( s => s.SolutionPackageReferences ) )
            {
                if( packages.TryGetValue( p.Target.Artifact, out LocalPackageItem? target ) )
                {
                    if( target.Project.Solution != p.Owner )
                    {
                        solutionDependencies.Add( (p.Owner,  target) );
                    }
                    else
                    {
                        // A Solution is referencing a Package that it generates...
                        // This would always use the "previous" package version, that would be stupid. 
                        // We simply totally skip this.
                        m.Warn( $"Solution '{p.Owner}' has its own project '{target.Project.Name}' as a Solution dependency ('{p.Target.Artifact}'). We ignore this (silly) reference." );
                    }
                }
                else
                {
                    // Solution dependency to an external Package.
                    externalRefs.Add( new PackageReference( p.Owner, p.Target ) );
                }
            }

            return new DependencyAnalyzer(
                        m,
                        solutionCtx,
                        solutionDependencies,
                        projectItems,
                        externalRefs,
                        traceGraphDetails );
        }

    }
}
