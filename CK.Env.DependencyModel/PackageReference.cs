using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Defines a dependency to a package.
    /// </summary>
    public readonly struct PackageReference
    {
        /// <summary>
        /// Gets the project that owns the reference.
        /// </summary>
        public IProject Owner { get; }

        /// <summary>
        /// Gets the referenced artifact instance.
        /// It is an installable type (see <see cref="ArtifactType.IsInstallable"/>).
        /// </summary>
        public ArtifactInstance Target { get; }

        /// <summary>
        /// Gets this reference kind.
        /// </summary>
        public ProjectDependencyKind Kind { get; }

        internal PackageReference( IProject o, ArtifactInstance t, ProjectDependencyKind kind )
        {
            Owner = o;
            Target = t;
            Kind = kind;
        }
    }
}
