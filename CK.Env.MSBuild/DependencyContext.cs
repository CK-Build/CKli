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
        readonly SolutionFile[] _solutions;
        readonly Dictionary<string, Package> _packages;
        readonly Dictionary<Project, ProjectItem> _projects;
        public class Package : IDependentItem, IDependentItemRef
        {
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
            /// and the versionless package identifier if the project is available.
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
        internal class ProjectItem : IDependentItem, IDependentItemRef
        {
            readonly Project _p;
            readonly List<IDependentItemRef> _requires;

            ProjectItem( Project p, Dictionary<Project, ProjectItem> cache )
            {
                _p = p;
                cache.Add( p, this );
                _requires = p.Deps.Projects.Select( dep => Create( dep.Project, cache ) ).ToList<IDependentItemRef>();
            }

            static public ProjectItem Create( Project p, Dictionary<Project,ProjectItem> cache )
            {
                if( cache.TryGetValue( p, out var item ) ) return item;
                return new ProjectItem( p, cache );
            }

            public Project Project => _p;

            /// <summary>
            /// Whether this project belongs to the Published ones.
            /// When a project is published its name is the one of the associated DependencyContext.Package
            /// and is necessarily unique: we can use its file name as the item's FullName (with the .csproj
            /// extension: the naked project name is its Package's FullName).
            /// But when a project is not published, we scope its name to its solution name to compute the
            /// FullName so that utility projects can have the same name in different solutions.
            /// </summary>
            public bool IsPublished { get; set; }

            public string FullName => IsPublished
                                        ? _p.Path.LastPart
                                        : _p.Solution.UniqueSolutionName + '/' + _p.Path.LastPart;

            internal void AddRequires( Package p ) => _requires.Add( p );

            public IDependentItemContainerRef Container => _p.Solution;

            public IDependentItemRef Generalization => null;

            public IEnumerable<IDependentItemRef> Requires => _requires;

            public IEnumerable<IDependentItemRef> RequiredBy => null;

            public IEnumerable<IDependentItemGroupRef> Groups => null;

            bool IDependentItemRef.Optional => false;

            public object StartDependencySort( IActivityMonitor m ) => _p;
        }

        DependencyContext( SolutionFile[] solutions, Dictionary<string, Package> packages, Dictionary<Project, ProjectItem> projects )
        {
            _solutions = solutions;
            _packages = packages;
            _projects = projects;
        }

        public SolutionDependencyResult AnalyzeDependencies( IActivityMonitor m, SolutionSortStrategy content )
        {
            List<SortableSolutionFile> sortables = new List<SortableSolutionFile>();
            foreach( var s in _solutions )
            {
                if( content != SolutionSortStrategy.EverythingExceptBuildProjects
                    && s.PrimarySolution != null )
                {
                    continue;
                }
                IEnumerable<ProjectItem> children;
                switch( content )
                {
                    case SolutionSortStrategy.PublishedProjects:
                        children = s.PublishedProjects.Select( p => _projects[p] );
                        break;
                    case SolutionSortStrategy.PublishedAndTestsProjects:
                        children = s.PublishedProjects.Concat( s.TestProjects ).Select( p => _projects[p] );
                        break;
                    default:
                        children = s.AllProjects.Except( s.BuildProjects ).Select( p => _projects[p] );
                        break;
                }
                sortables.Add( new SortableSolutionFile( s, children ) );
            }
            IDependencySorterResult result = DependencySorter.OrderItems( m, sortables, null );
            if( !result.IsComplete )
            {
                return new SolutionDependencyResult( content, result );
            }
            var table = result.SortedItems
                          // 1 - Selects solutions along with their ordered index.
                          .Where( sorted => sorted.GroupForHead == null && sorted.Item is SortableSolutionFile )
                          .Select( ( sorted, idx ) =>
                                        (
                                            Index: idx,
                                            Solution: (SolutionFile)sorted.StartValue,
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
                                      );
            return new SolutionDependencyResult( content, result, table.ToList() );

        }

        static public DependencyContext Create( IActivityMonitor m, IEnumerable<SolutionFile> solutionFiles )
        {
            var packages = new Dictionary<string, Package>();
            var solutions = solutionFiles.ToArray();
            var projectItems = new Dictionary<Project, ProjectItem>();
            // 1 - Create all the ProjectItem for all projects in all solutions.
            foreach( var s in solutions )
            {
                if( !s.InitializeProjectsDeps( m ) ) return null;
                foreach( var project in s.AllProjects )
                {
                    ProjectItem.Create( project, projectItems );
                }
            }
            // 2 - Create a Package for each Published project.
            foreach( var s in solutions )
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
                }
            }
            // 3 - Create the requirements between each project and either
            //     a Package that is bound to a Published project or to an
            //     external Package.
            foreach( var project in projectItems.Values )
            {
                foreach( var dep in project.Project.Deps.Packages )
                {
                    if( !packages.TryGetValue( dep.PackageId, out var target ) )
                    {
                        target = RegisterExternal( packages, dep );
                    }
                    project.AddRequires( target );
                }
            }
            return new DependencyContext( solutions, packages, projectItems );
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
