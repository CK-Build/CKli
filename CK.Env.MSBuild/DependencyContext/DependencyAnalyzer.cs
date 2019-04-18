using CK.Core;
using CK.Setup;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Root class of dependency analysis.
    /// A DependencyAnalyzer can be created on any set of <see cref="Solution"/> thanks to the
    /// factory method <see cref="Create(IActivityMonitor, IEnumerable{Solution})"/>.
    /// </summary>
    public class DependencyAnalyser
    {
        readonly Solution[] _solutions;
        readonly Dictionary<string, DotNetPackageItem> _packages;
        readonly DotNetProjectItem.Cache _projects;
        readonly Dictionary<string, NPMPackageItem> _npmPackages;
        readonly NPMProjectItem.Cache _npmProjects;
        readonly CKTrait _frameworks;
        ProjectDependencyResult _projectDependencies;

        /// <summary>
        /// Package items are not bound to any container (solution). There is 2 kind of packages:
        /// locally produced (the item requires the Solution that owns its source code), and external
        /// packages.
        /// </summary>
        internal class DotNetPackageItem : IDotNetDependentPackage, IDependentItem, IDependentItemRef
        {
            // This contains only the Solution of the Project for local projects (the Package -published- requires the
            // Solution that owns its source code.)
            // Otherwise, for external packages, this is null (no requirements).
            readonly IDependentItemRef[] _requires;

            internal DotNetPackageItem( DotNetProjectItem localProject )
            {
                _requires = new IDependentItemRef[] { localProject.Project.Solution };
                Project = localProject.Project;
                Version = null;
                PackageId = Project.Name;
                FullName = Project.Name;
            }

            internal DotNetPackageItem( string packageId, SVersion v, string fullName )
            {
                PackageId = packageId;
                Version = v;
                FullName = fullName;
            }

            /// <summary>
            /// Gets the local project that produces this package.
            /// This is null for external packages: for external package this <see cref="FullName"/> is the <see cref="ProjectBase.Name"/>
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
        /// Package items are not bound to any container (solution). There is 2 kind of packages:
        /// locally produced (the item requires the Solution that owns its source code), and external
        /// packages.
        /// </summary>
        internal class NPMPackageItem : INPMDependentPackage, IDependentItem, IDependentItemRef
        {
            // This contains only the Solution of the Project for local projects (the Package -published- requires the
            // Solution that owns its source code.)
            // Otherwise, for external packages, this is null (no requirements).
            readonly IDependentItemRef[] _requires;

            internal NPMPackageItem( NPMProjectItem localProject )
            {
                _requires = new IDependentItemRef[] { localProject.Project.Solution };
                Project = localProject.Project;
                Version = null;
                PackageId = Project.PackageJson.Name;
                FullName = Project.PackageJson.Name;
            }

            internal NPMPackageItem( string packageId, SVersion v, string fullName )
            {
                PackageId = packageId;
                Version = v;
                FullName = fullName;
            }

            /// <summary>
            /// Gets the local project that produces this package.
            /// This is null for external packages: for external package this <see cref="FullName"/> is the <see cref="ProjectBase.Name"/>
            /// and this <see cref="Version"/> is null.
            /// </summary>
            public NPMProject Project { get; }

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
        /// This internal class is the IDependentItem that represents a DotNet/NuGet Project
        /// in a SortableSolutionFile graph.
        /// </summary>
        internal class DotNetProjectItem : IDotNetDependentProject, IDependentItem, IDependentItemRef
        {
            readonly Project _p;
            readonly List<IDependentItemRef> _requires;
            readonly Cache _cache;

            public class Cache
            {
                readonly Dictionary<Project, DotNetProjectItem> _cache;

                public Cache()
                {
                    _cache = new Dictionary<Project, DotNetProjectItem>();
                }

                public DotNetProjectItem this[Project p] => _cache[p];

                public IEnumerable<Project> AllProjects => _cache.Keys;

                public IEnumerable<DotNetProjectItem> AllProjectItems => _cache.Values;

                /// <summary>
                /// Dirty trick: when set to true this property forces all ProjectItem's <see cref="IDependentItem.Container"/>
                /// to be null and <see cref="IDependentItem.Requires"/> to contain the ProjectItem requirements instead of
                /// the <see cref="IDotNetDependentPackage"/> requirements for locally published packages.
                /// We use this to reuse ProjectItems to compute Build projects graph dependencies
                /// (this graph ignores the solutions and focuses only on projects).
                /// </summary>
                public bool PureProjectsMode { get; set; }

                internal void Add( Project p, DotNetProjectItem pItem ) => _cache.Add( p, pItem );

                public DotNetProjectItem Create( Project p )
                {
                    if( _cache.TryGetValue( p, out var item ) ) return item;
                    return new DotNetProjectItem( p, this );
                }

            }

            DotNetProjectItem( Project p, Cache cache )
            {
                _p = p;
                _cache = cache;
                cache.Add( p, this );
                _requires = p.Deps.Projects.Select( dep => cache.Create( dep.TargetProject ) ).ToList<IDependentItemRef>();
            }

            /// <summary>
            /// Gets the project itself.
            /// </summary>
            public Project Project => _p;

            /// <summary>
            /// Whether this project belongs to the Published ones.
            /// When a project is published, to distinguish it from the associated necessarily
            /// unique DependencyContext.Package we use its file name as the item's FullName (with the .csproj
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

            /// <summary>
            /// Gets the name of the package produced by this project or null when <see cref="IsPublished"/>
            /// is false.
            /// When not null, this is the nameof the project folder.
            /// </summary>
            public string PublishedName => IsPublished
                                        ? _p.Path.Parts[_p.Path.Parts.Count - 2]
                                        : null;

            internal void AddRequires( IDependentItemRef p ) => _requires.Add( p );

            string IDependentProject.Type => "NuGet";

            IDependentItemContainerRef IDependentItem.Container => _cache.PureProjectsMode
                                                                    ? null
                                                                    : (_p.Solution.SpecialType == SolutionSpecialType.IncludedSecondarySolution
                                                                        ? _p.Solution.PrimarySolution
                                                                        : _p.Solution);

            IDependentItemRef IDependentItem.Generalization => null;

            IEnumerable<IDependentItemRef> IDependentItem.Requires
            {
                get
                {
                    if( _cache.PureProjectsMode )
                    {
                        return _requires.Select( d => d is IDotNetDependentPackage p && p.Project != null ? _cache[p.Project] : d );
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

        /// <summary>
        /// This internal class is the IDependentItem that represents a NPM Project
        /// in a SortableSolutionFile graph.
        /// </summary>
        internal class NPMProjectItem : INPMDependentProject, IDependentItem, IDependentItemRef
        {
            readonly NPMProject _p;
            readonly List<IDependentItemRef> _requires;
            readonly Cache _cache;

            public class Cache
            {
                readonly Dictionary<NPMProject, NPMProjectItem> _cache;

                public Cache()
                {
                    _cache = new Dictionary<NPMProject, NPMProjectItem>();
                }

                public NPMProjectItem this[NPMProject p] => _cache[p];

                public IEnumerable<NPMProject> AllProjects => _cache.Keys;

                public IEnumerable<NPMProjectItem> AllProjectItems => _cache.Values;

                public int Count => _cache.Count;

                /// <summary>
                /// Dirty trick: when set to true this property forces all ProjectItem's <see cref="IDependentItem.Container"/>
                /// to be null and <see cref="IDependentItem.Requires"/> to contain the ProjectItem requirements instead of
                /// the <see cref="IDotNetDependentPackage"/> requirements for locally published packages.
                /// We use this to reuse ProjectItems to compute Build projects graph dependencies
                /// (this graph ignores the solutions and focuses only on projects).
                /// </summary>
                public bool PureProjectsMode { get; set; }

                internal void Add( NPMProject p, NPMProjectItem pItem ) => _cache.Add( p, pItem );

                public NPMProjectItem Create( NPMProject p )
                {
                    if( _cache.TryGetValue( p, out var item ) ) return item;
                    return new NPMProjectItem( p, this );
                }

            }

            NPMProjectItem( NPMProject p, Cache cache )
            {
                _p = p;
                _cache = cache;
                cache.Add( p, this );
                _requires = p.ProjectDependencies.Select( dep => cache.Create( dep.Project ) )
                             .ToList<IDependentItemRef>();
            }

            /// <summary>
            /// Gets the project itself.
            /// </summary>
            public NPMProject Project => _p;

            /// <summary>
            /// Whether this project is published.
            /// When a project is published, this FullName is the "Package name/package.json"
            /// (the package itself uses the naked "Package name" as tis FullName).
            /// When NOT published, we use the "Solution name/<see cref="NPM.PackageJsonFile.SafeName"/>".
            /// </summary>
            public bool IsPublished => _p.PackageJson.IsPublished;

            /// <summary>
            /// Gets the full name of this project item (see <see cref="IsPublished"/>).
            /// </summary>
            public string FullName => IsPublished
                                        ? _p.PackageJson.Name + "/package.json"
                                        : _p.Solution.UniqueSolutionName + '/' + _p.PackageJson.SafeName;

            /// <summary>
            /// Gets the name of the package produced by this project or null when <see cref="IsPublished"/> is false.
            /// When not null, this is the <see cref="NPM.PackageJsonFile.Name"/>.
            /// </summary>
            public string PublishedName => IsPublished
                                        ? _p.PackageJson.Name
                                        : null;

            internal void AddRequires( IDependentItemRef p ) => _requires.Add( p );

            string IDependentProject.Type => "NuGet";

            IDependentItemContainerRef IDependentItem.Container => _cache.PureProjectsMode
                                                                    ? null
                                                                    : _p.Solution;

            IDependentItemRef IDependentItem.Generalization => null;

            IEnumerable<IDependentItemRef> IDependentItem.Requires
            {
                get
                {
                    if( _cache.PureProjectsMode )
                    {
                        return _requires.Select( d => d is INPMDependentPackage p && p.Project != null
                                                        ? _cache[p.Project]
                                                        : d );
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

        DependencyAnalyser(
            string uniqueBranchName,
            Solution[] solutions,
            Dictionary<string, DotNetPackageItem> packages,
            DotNetProjectItem.Cache projects,
            Dictionary<string, NPMPackageItem> npmPackages,
            NPMProjectItem.Cache npmProjects,
            CKTrait frameworks )
        {
            UniqueBranchName = uniqueBranchName;
            _solutions = solutions;
            _packages = packages;
            _projects = projects;
            _npmPackages = npmPackages;
            _npmProjects = npmProjects;
            _frameworks = frameworks;
        }

        /// <summary>
        /// Gets the unique branch name from which all solutions have been analyzed.
        /// If solutions were in more than one branch, this is null.
        /// </summary>
        public string UniqueBranchName { get; }

        /// <summary>
        /// Gets the set of solutions.
        /// </summary>
        public IReadOnlyCollection<Solution> Solutions => _solutions;

        /// <summary>
        /// Gets project dependency information.
        /// </summary>
        public ProjectDependencyResult ProjectDependencies
        {
            get
            {
                if( _projectDependencies != null ) return _projectDependencies;

                DotNetPackageItem FindPackage( DeclaredPackageDependency d, bool isIncludedSolutionProject )
                {
                    // Check for a published Package.
                    if( _packages.TryGetValue( d.PackageId, out DotNetPackageItem p ) )
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
                        p = _packages[d.Package.ToString()];
                    }
                    return p;
                }

                // Computes ProjectDepencyRows (the ProjectDependencyResult.DependencyTable).
                var allDeps = _projects.AllProjects.SelectMany( p => p.Deps.Packages )
                                    .Select( d => (Dep: d, Target: FindPackage( d, d.Owner.Solution.SpecialType == SolutionSpecialType.IncludedSecondarySolution )) )
                                    .Where( t => t.Target != null )
                                    .Select( t => new ProjectDependencyResult.ProjectDepencyRow(
                                                    t.Dep,
                                                    _projects[t.Dep.Owner],
                                                    t.Target ) )
                                    .ToList();
                // Group by framework (the ProjectDependencyResult.PerFrameworkDependencies). 
                var result = new List<ProjectDependencyResult.FrameworkDependencies>();
                foreach( var f in _frameworks.AtomicTraits )
                {
                    var fDeps = allDeps.Where( d => d.RawPackageDependency.Frameworks.Intersect( f ) == f ).ToList();
                    result.Add( new ProjectDependencyResult.FrameworkDependencies( f, fDeps ) );
                }
                return _projectDependencies = new ProjectDependencyResult( allDeps, result );
            }
        }

        /// <summary>
        /// Gets the information about build projects (see <see cref="Project.IsBuildProject"/>)
        /// and their dependencies.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The build projects information.</returns>
        BuildProjectsInfo GetBuildProjectInfo( IActivityMonitor m )
        {
            Debug.Assert( !_projects.PureProjectsMode );
            _projects.PureProjectsMode = true;
            try
            {
                IDependencySorterResult  rBuildProjects = DependencySorter.OrderItems( m, _projects.AllProjectItems.Where( p => p.Project.IsBuildProject ), null );
                if( !rBuildProjects.IsComplete )
                {
                    rBuildProjects.LogError( m );
                    return new BuildProjectsInfo( rBuildProjects, null );
                }
                else
                {
                    var rankedProjects = rBuildProjects.SortedItems
                                                .Where( i => i.Item is IDotNetDependentProject )
                                                .Select( i => (i.Rank,
                                                               Project: (IDotNetDependentProject)i.Item,
                                                               DirectDeps: i.Requires
                                                                            .Select( s => s.Item )
                                                                            .OfType<IDotNetDependentProject>(),
                                                               AllDeps: i.GetAllRequires()
                                                                         .Select( s => s.Item )
                                                                         .OfType<IDotNetDependentProject>()) );

                    var zeroBuildProjects = rankedProjects.Select( (p,idx) => new ZeroBuildProjectInfo(
                                    idx,
                                    p.Rank,
                                    p.Project.Project.Solution.UniqueSolutionName,
                                    p.Project.Project.Name,
                                    p.Project.Project.PrimarySolutionRelativeFolderPath,
                                    p.Project.IsPublished,
                                    // UpgradePackages: Among direct dependencies, consider only the
                                    //                  published projects and the ones who are actually referenced
                                    //                  as a package (ignores ProjectReference).
                                    p.DirectDeps
                                        .Where( d => d.IsPublished
                                                     && p.Project.Project
                                                         .Deps.Packages.Any( dep => dep.PackageId == d.Project.Name ) )
                                        .Select( d => d.Project.Name )
                                        .ToArray(),
                                    // UpgradeZeroProjects: Among direct dependencies, consider all the
                                    //                      published projects, the ones who are actually referenced
                                    //                      as a package AND the ones that are ProjectReference.
                                    //                      ProjectReference MUST be transformed into PackageReference
                                    //                      during ZeroBuild.
                                    p.DirectDeps
                                        .Where( d => d.IsPublished )
                                        .Select( d => d.Project.Name )
                                        .ToArray(),

                                    // Dependencies: Considers all the projects and computes their ZeroBuildProjectInfo.FullName. 
                                    p.AllDeps
                                            .Select( d => d.IsPublished
                                                            ? d.Project.Name
                                                            : d.Project.Solution.UniqueSolutionName + '/' + d.Project.Name )
                                            .ToArray()

                                    ) )
                        .ToArray();

                    Debug.Assert( zeroBuildProjects.Select( z => z.Rank ).IsSortedLarge() );
                    Debug.Assert( zeroBuildProjects.Select( z => z.Index ).IsSortedStrict() );

                    return new BuildProjectsInfo( rBuildProjects, zeroBuildProjects );
                }
            }
            finally
            {
                _projects.PureProjectsMode = false;
            }
        }

        /// <summary>
        /// Creates a new <see cref="SolutionDependencyContext"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="strategy">The strategy.</param>
        /// <returns>The context or null on error.</returns>
        public SolutionDependencyContext CreateDependencyContext( IActivityMonitor m, SolutionSortStrategy strategy = SolutionSortStrategy.EverythingExceptBuildProjects )
        {
            var sortables = new Dictionary<Solution, SortableSolutionFile>();
            // Handles Primary solutions first.
            foreach( var s in _solutions )
            {
                if( s.PrimarySolution != null ) continue;
                sortables.Add( s, new SortableSolutionFile( s, GetProjectItems( s, strategy ) ) );
            }
            if( strategy == SolutionSortStrategy.EverythingExceptBuildProjects )
            {
                // Handles Secondary solutions.
                foreach( var s in _solutions )
                {
                    if( s.PrimarySolution == null ) continue;
                    if( s.SpecialType == SolutionSpecialType.IncludedSecondarySolution )
                    {
                        var primary = sortables[s.PrimarySolution];
                        primary.AddSecondaryProjects( GetProjectItems( s, strategy ) );
                    }
                    else
                    {
                        sortables.Add( s, new SortableSolutionFile( s, GetProjectItems( s, strategy ) ) );
                    }
                }
            }

            IDependencySorterResult result = DependencySorter.OrderItems( m, sortables.Values, null );
            if( !result.IsComplete )
            {
                return new SolutionDependencyContext( UniqueBranchName, strategy, result, ProjectDependencies, GetBuildProjectInfo( m ) );
            }
            // Building the list of SolutionDependencyContext.DependencyRow.
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
                                                                            .OfType<DotNetPackageItem>()
                                                                            .Where( package => package.Project != null )
                                                                            .Select( package => (Origin: c.Project, Target: package) )
                                        )) )
                           // 3 - Second Map: Expands the LocalRefs.
                           .SelectMany( s => s.LocalRefs.Any()
                                                ? s.LocalRefs.Select( r => new SolutionDependencyContext.DependencyRow
                                                                (
                                                                    s.Index,
                                                                    s.Solution,
                                                                    r.Origin,
                                                                    r.Target.Project
                                                                ) )
                                                : new[] { new SolutionDependencyContext.DependencyRow( s.Index, s.Solution, null, null ) }
                                      )
                            .ToList();
            Debug.Assert( table.Select( r => r.Index ).IsSortedLarge() );

            // Now that the table of SolutionDependencyContext.DependencyRow is built, use it to compute the
            // pure solution dependency graph.
            //
            var indexByName = new Dictionary<string, SolutionDependencyContext.DependentSolution>();
            Solution current = null;
            var depSolutions = new SolutionDependencyContext.DependentSolution[sortables.Count];
            foreach( var r in table )
            {
                if( current != r.Solution )
                {
                    current = r.Solution;
                    var newDependent = new SolutionDependencyContext.DependentSolution( current, r.Index, ProjectDependencies, table, s => indexByName[ s.UniqueSolutionName ] );
                    depSolutions[r.Index] = newDependent;
                    indexByName.Add( current.UniqueSolutionName, newDependent );
                }
            }
            return new SolutionDependencyContext( UniqueBranchName, indexByName, strategy, result, ProjectDependencies, table, depSolutions, GetBuildProjectInfo( m ) );
        }

        IEnumerable<IDependentItemRef> GetProjectItems( Solution s, SolutionSortStrategy content )
        {
            IEnumerable<IDependentItemRef> dotNetItems;
            switch( content )
            {
                case SolutionSortStrategy.PublishedProjects:
                    dotNetItems = s.PublishedProjects.Select( p => _projects[p] );
                    break;
                case SolutionSortStrategy.PublishedAndTestsProjects:
                    dotNetItems = s.PublishedProjects.Concat( s.TestProjects ).Distinct().Select( p => _projects[p] );
                    break;
                case SolutionSortStrategy.EverythingExceptBuildProjects:
                    dotNetItems = s.AllProjects.Except( s.BuildProjects ).Select( p => _projects[p] );
                    break;
                default: throw new Exception( "Unsuported SolutionSortStrategy" );
            }
            return dotNetItems.Concat( s.NPMProjects.Select( p => _npmProjects[p] ) );
        }

        /// <summary>
        /// Factory method for a <see cref="DependencyAnalyser"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="solutionFiles">The set of <see cref="Solution"/> to consider.</param>
        /// <returns>A new dependency context.</returns>
        public static DependencyAnalyser Create( IActivityMonitor m, IEnumerable<Solution> solutionFiles )
        {
            var packages = new Dictionary<string, DotNetPackageItem>();
            Dictionary<string, NPMPackageItem> npmPackages;
            var solutions = solutionFiles.ToArray();
            var projectItems = new DotNetProjectItem.Cache();
            var npmProjectItems = new NPMProjectItem.Cache();
            var frameworks = MSBuildContext.Traits.EmptyTrait;
            // Note: Project to project references are translated into Requirements directly
            //       in the ProjectItem constructor.
            //       After having built the ProjectItem, we handle here the Packages (and PackageReferences between
            //       projects).
            string uniqueBranchName = null;
            int solutionCount = 0;
            var branchNames = new List<string>();
            using( m.OpenDebug( "Creating all the ProjectItem for all projects in all solutions." ) )
            {
                foreach( var s in solutions )
                {
                    ++solutionCount;
                    if( !branchNames.Contains( s.BranchName ) ) branchNames.Add( s.BranchName );
                    using( m.OpenDebug( $"Solution {s.UniqueSolutionName}." ) )
                    {
                        foreach( var project in s.AllProjects )
                        {
                            frameworks = frameworks.Union( project.TargetFrameworks );
                            projectItems.Create( project );
                        }
                        // NPM projects.
                        foreach( var npm in s.NPMProjects )
                        {
                            npmProjectItems.Create( npm );
                        }
                    }
                }
                if( branchNames.Count > 1 )
                {
                    m.Warn( $"Dependency analyzer created on {solutionCount} solutions from more than one branch: {branchNames.Concatenate()}" );
                }
                else if( branchNames.Count > 0 )
                {
                    uniqueBranchName = branchNames[0];
                }
            }
            using( m.OpenDebug( "Creating Package for all Published projects in all solutions." ) )
            {
                foreach( var s in solutions )
                {
                    using( m.OpenDebug( $"Solution {s.UniqueSolutionName}." ) )
                    {
                        foreach( var p in s.PublishedProjects )
                        {
                            if( packages.TryGetValue( p.Name, out var alreadyPublished ) )
                            {
                                m.Error( $"{p} is already published: {alreadyPublished}." );
                                return null;
                            }
                            DotNetProjectItem projectItem = projectItems[p];
                            projectItem.IsPublished = true;
                            packages.Add( p.Name, new DotNetPackageItem( projectItem ) );
                            m.Debug( $"Package {p.Name} created." );
                        }
                    }
                }
            }
            // 3 - Create the requirements between each project and either
            //     a Package that is bound to a Published project or to an
            //     external Package.
            //     For projects that belong to a secondary solution whose SpecialType is
            //     IncludedSecondarySolution, we depends on the project instead of the published Package.
            foreach( var project in projectItems.AllProjectItems )
            {
                // Consider package references (Project to Project references are handled by ProjectItem constructors).
                foreach( var dep in project.Project.Deps.Packages )
                {
                    IDependentItemRef refTarget;
                    if( packages.TryGetValue( dep.PackageId, out DotNetPackageItem target ) )
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
            using( m.OpenInfo( $"Handling {npmProjectItems.Count} NPM projects." ) )
            {
                npmPackages = npmProjectItems.AllProjectItems
                            .Where( npm => npm.IsPublished )
                            .ToDictionary( npm => npm.PublishedName, npm => new NPMPackageItem( npm ) );
                m.Info( $"Found {npmPackages.Count} published packages." );

                foreach( var npmProject in npmProjectItems.AllProjectItems )
                {
                    foreach( var dep in npmProject.Project.PackageJson.Dependencies )
                    {
                        if( dep.Type != NPM.VersionDependencyType.MinVersion )
                        {
                            if( dep.Type != NPM.VersionDependencyType.LocalPath )
                            {
                                m.Warn( $"Ignored {dep} dependency in {npmProject.Project.PackageJson.FilePath}." );
                            }
                            continue;
                        }
                        IDependentItemRef refTarget;
                        if( npmPackages.TryGetValue( dep.Name, out NPMPackageItem target ) )
                        {
                            // Dependency to a Published projects from the primary solution are
                            // transfomed into requirements to the Project itself.
                            refTarget = target;
                            if( target.Project.Solution == npmProject.Project.Solution )
                            {
                                refTarget = npmProjectItems[target.Project];
                            }
                        }
                        else
                        {
                            // Dependency to an external Package.
                            refTarget = RegisterExternal( npmPackages, dep );
                        }
                        npmProject.AddRequires( refTarget );
                    }
                }
            }
            return new DependencyAnalyser(
                        uniqueBranchName,
                        solutions,
                        packages,
                        projectItems,
                        npmPackages,
                        npmProjectItems,
                        frameworks );
        }

        static NPMPackageItem RegisterExternal( Dictionary<string, NPMPackageItem> externals, NPM.NPMDep dep )
        {
            string fullName = "NPM:" + dep.Name+ '/' + dep.MinVersion.ToNuGetPackageString();
            if( !externals.TryGetValue( fullName, out var p ) )
            {
                p = new NPMPackageItem( dep.Name, dep.MinVersion, fullName );
                externals.Add( fullName, p );
            }
            return p;
        }

        static DotNetPackageItem RegisterExternal( Dictionary<string, DotNetPackageItem> externals, DeclaredPackageDependency dep )
        {
            string fullName = "NuGet:" + dep.PackageId + '/' + dep.Version.ToString();
            if( !externals.TryGetValue( fullName, out var p ) )
            {
                p = new DotNetPackageItem( dep.PackageId, dep.Version, fullName );
                externals.Add( fullName, p );
            }
            return p;
        }

    }
}
