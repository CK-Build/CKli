using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Encapsulates the set of dependencies that exist in a <see cref="DependencyContext"/>.
    /// </summary>
    public class ProjectDependencyResult
    {
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
            /// Note that if is is a locally published package, this <see cref="IDependentPackage"/>
            /// will have a null version. This <see cref="Version"/> property should be used
            /// since it is the one of the <see cref="RawPackageDependency"/>.
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

            public override string ToString() => RawPackageDependency.ToString();
        }

        /// <summary>
        /// Gets all dependencies.
        /// </summary>
        public IReadOnlyList<ProjectDepencyRow> DependencyTable { get; }

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

            internal FrameworkDependencies(CKTrait f, IReadOnlyList<ProjectDepencyRow> table )
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

        internal ProjectDependencyResult( IReadOnlyList<ProjectDepencyRow> all, IReadOnlyList<FrameworkDependencies> perFramework )
        {
            DependencyTable = all;
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
