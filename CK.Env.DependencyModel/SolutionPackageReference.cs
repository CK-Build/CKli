using CK.Core;
using CK.Build;
using System;
using System.Diagnostics;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Defines a direct dependency to a package from a <see cref="ISolution"/>.
    /// </summary>
    public readonly struct SolutionPackageReference
    {
        /// <summary>
        /// Gets the solution that owns the reference.
        /// </summary>
        public ISolution Owner { get; }

        /// <summary>
        /// Gets the referenced artifact instance.
        /// It is an installable type (see <see cref="ArtifactType.IsInstallable"/>).
        /// </summary>
        public ArtifactInstance Target { get; }

        internal SolutionPackageReference( ISolution o, ArtifactInstance t )
        {
            Owner = o;
            Target = t;
        }

        /// <summary>
        /// Returns "Owner -> Target" string.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => Owner.ToString() + " -> " + Target.ToString();
    }
}
