using CK.Core;
using CK.Env.Plugins;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.NPM
{
    public class PackageJsonFile : JsonFileBase
    {
        readonly List<NPMDep> _deps;

        internal PackageJsonFile( NPMProject p )
            : base( p.FileSystem, p.FullPath.AppendPart( "package.json" ) )
        {
            _deps = new List<NPMDep>();
        }

        /// <summary>
        /// Gets or sets the name.
        /// Can be null: a non-published package does not require a name (nor <see cref="Version"/>).
        /// </summary>
        public string Name
        {
            get => (string)Root["name"];
            set => SetNonNullProperty( Root, "name", value );
        }

        /// <summary>
        /// Gets or sets whether this package is private: even if it has a <see cref="Name"/>
        /// and a <see cref="Version"/>, it must not be published.
        /// </summary>
        public bool IsPrivate
        {
            get => (bool?)Root["private"] ?? false;
            set => Root["private"] = value;
        }

        /// <summary>
        /// Gets whether this package is published.
        /// It must not be private and have a name and a version.
        /// </summary>
        public bool IsPublished => !IsPrivate && Name != null && Version != null;

        /// <summary>
        /// Gets or sets the version.
        /// Can be null: a non-published package does not require any version (nor <see cref="Name"/>).
        /// </summary>
        public SVersion Version
        {
            get
            {
                var v = (string)Root["version"];
                return v != null ? SVersion.TryParse( v ) : null;
            }
            set => SetNonNullProperty( Root, "version", value?.ToNuGetPackageString() );
        }

        /// <summary>
        /// Gets the list of dependencies.
        /// </summary>
        public IReadOnlyList<NPMDep> Dependencies { get; }

        /// <summary>
        /// Sets a minimum version for a dependency that must exist, regardless of it current status.
        /// </summary>
        /// <param name="name">The dependency name that must appear in <see cref="Dependencies"/>.</param>
        /// <param name="v">The minimal version. Must not be null.</param>
        public void SetDependencyMinVersion( string name, SVersion v )
        {
        }

        /// <summary>
        /// Ensures that a dependency exist with a given minimal version.
        /// </summary>
        /// <param name="name">The dependency name.</param>
        /// <param name="v">The minimal version. Must not be null.</param>
        /// <param name="kind">The kind of the dependency.</param>
        public void EnsureDependency( string name, SVersion v, DependencyKind kind )
        {
        }

        internal NPMProjectStatus Refresh( IActivityMonitor m )
        {           
            return NPMProjectStatus.Valid;
        }
    }
}
