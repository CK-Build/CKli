using CK.Core;
using CSemVer;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Encapsulates the set of dependencies that exist in a <see cref="DependencyAnalyser"/>.
    /// </summary>
    public class ProjectDependencyResult
    {
        readonly ProjectFrameworkCache _projectFrameworkCache;
        PackageDependencyAnalysisResult _allDeps;
        PackageDependencyAnalysisResult _externalDeps;
        PackageDependencyAnalysisResult _localDeps;

        /// <summary>
        /// Exposes a dependency from a source project to a package, be it locally produced
        /// or external.
        /// </summary>
        public class ProjectDepencyRow
        {
            /// <summary>
            /// Direct access to the <see cref="DeclaredPackageDependency"/>.
            /// </summary>
            public DeclaredPackageDependency RawPackageDependency { get; }

            /// <summary>
            /// Gets whether this dependency is to an external package and not a package that is
            /// locally produced.
            /// </summary>
            public bool IsExternalDependency => TargetPackage.Project == null;

            /// <summary>
            /// Gets the source project.
            /// </summary>
            public IDependentProject SourceProject { get; }

            /// <summary>
            /// Gets the target package.
            /// Note that if is is a locally published package, this TargetPackage
            /// will have a null <see cref="IDependentPackage.Version"/>.
            /// The <see cref="Version"/> property should be used since it is the one of the <see cref="RawPackageDependency"/>.
            /// </summary>
            public IDependentPackage TargetPackage { get; }

            /// <summary>
            /// Gets the target package identifier.
            /// </summary>
            public string PackageId => RawPackageDependency.PackageId;

            /// <summary>
            /// Gets the target package version.
            /// </summary>
            public SVersion Version => RawPackageDependency.Version;

            internal ProjectDepencyRow(
                DeclaredPackageDependency d,
                IDependentProject s,
                IDependentPackage t )
            {
                Debug.Assert( d.PackageId == t.PackageId );
                Debug.Assert( d.Owner == s.Project );
                RawPackageDependency = d;
                SourceProject = s;
                TargetPackage = t;
            }

            public override string ToString() => (IsExternalDependency ? "" : "Local: ") + RawPackageDependency.ToString();
        }

        /// <summary>
        /// Gets all dependencies.
        /// </summary>
        public IReadOnlyList<ProjectDepencyRow> DependencyTable { get; }

        /// <summary>
        /// Gets the <see cref="PackageDependencyAnalysisResult"/> for all packages (local and external).
        /// </summary>
        public PackageDependencyAnalysisResult AllPackageDependencies => _allDeps ?? (_allDeps = ComputeExternalDependencies( null ));

        /// <summary>
        /// Gets the <see cref="PackageDependencyAnalysisResult"/> for external packages.
        /// </summary>
        public PackageDependencyAnalysisResult ExternalPackageDependencies => _externalDeps ?? (_externalDeps = ComputeExternalDependencies( true ));

        /// <summary>
        /// Gets the <see cref="PackageDependencyAnalysisResult"/> for local packages.
        /// </summary>
        public PackageDependencyAnalysisResult LocalPackageDependencies => _localDeps ?? (_localDeps = ComputeExternalDependencies( false ));

        PackageDependencyAnalysisResult ComputeExternalDependencies( bool? externalDependencies = null )
        {
            var monoVersions = new List<(VersionedPackage Package, IReadOnlyList<IDependentProject> Projects)>();
            var multiVersions = new List<(VersionedPackage Package, IReadOnlyList<IProjectFramework> Projects)>();

            IEnumerable<ProjectDepencyRow> all = DependencyTable;
            if( externalDependencies.HasValue ) all = all.Where( r => r.IsExternalDependency == externalDependencies.Value );

            foreach( var (PackageId, ByVersion) in all.GroupBy( d => d.PackageId )
                                   .Select( g => (PackageId: g.Key, ByVersion: g.GroupBy( r => r.Version )) ) )
            {
                var count = ByVersion.Count();
                Debug.Assert( count > 0 );
                if( count == 1 ) monoVersions.Add( (ByVersion.First().First().RawPackageDependency.Package, ByVersion.First().Select( r => r.SourceProject ).Distinct().ToArray() ) );
                else
                {
                    foreach( var v in ByVersion )
                    {
                        VersionedPackage p = new VersionedPackage( PackageId, v.Key );
                        IReadOnlyList<IProjectFramework> refs = _projectFrameworkCache.Create( v ).ToList();
                        multiVersions.Add( (p, refs) );
                    }
                }
            }
            return new PackageDependencyAnalysisResult( externalDependencies, monoVersions, multiVersions );
        }

        /// <summary>
        /// Captures <see cref="DeclaredPackageDependency"/> filtered by applicable target framework.
        /// </summary>
        public class FrameworkDependencies
        {
            /// <summary>
            /// Gets the target framework (<see cref="CKTrait.IsAtomic"/> is true).
            /// </summary>
            public CKTrait Framework { get; }

            /// <summary>
            /// Gets the details of the project dependencies.
            /// </summary>
            public IReadOnlyList<ProjectDepencyRow> DependencyTable { get; }

            internal FrameworkDependencies( CKTrait f, IReadOnlyList<ProjectDepencyRow> table )
            {
                Debug.Assert( f.IsAtomic && !f.IsEmpty );
                Framework = f;
                DependencyTable = table;
            }
        }

        /// <summary>
        /// Gets the dependencies per target frameworks.
        /// </summary>
        public IReadOnlyList<FrameworkDependencies> PerFrameworkDependencies { get; }

        /// <summary>
        /// Gets the dependencies per target frameworks that references more than one version
        /// of a package.
        /// </summary>
        public IReadOnlyList<FrameworkDependencies> VersionDiscrepancies { get; }

        internal ProjectDependencyResult(
            IReadOnlyList<ProjectDepencyRow> all,
            IReadOnlyList<FrameworkDependencies> perFramework )
        {
            DependencyTable = all;
            _projectFrameworkCache = new ProjectFrameworkCache( all );
            PerFrameworkDependencies = perFramework;
            VersionDiscrepancies = perFramework.Select( r => new FrameworkDependencies
                                             (
                                               r.Framework,
                                               r.DependencyTable
                                                .GroupBy( x => x.PackageId )
                                                .Where( g => g.GroupBy( x => x.Version ).Distinct().Count() > 1 )
                                                .SelectMany( g => g ).ToList()
                                              ) )
                                       .Where( r => r.DependencyTable.Count > 0 )
                                       .ToList();
        }

    }
}
