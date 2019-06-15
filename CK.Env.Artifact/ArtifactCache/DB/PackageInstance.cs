using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Immutable model of a package (packages are installable artifacts).
    /// </summary>
    public class PackageInstance : IEquatable<PackageInstance>, IComparable<PackageInstance>
    {
        /// <summary>
        /// The reference from a <see cref="PackageInstance"/> to another one.
        /// </summary>
        public readonly struct Reference
        {
            /// <summary>
            /// Get the target of this reference.
            /// </summary>
            public PackageInstance Target { get; }

            /// <summary>
            /// Get the kind of dependency to <see cref="Target"/>.
            /// </summary>
            public ArtifactDependencyKind DependencyKind { get; }

            /// <summary>
            /// Gets the savors that, when not null, is a subset of the <see cref="PackageInstance.Savors"/> (or all the
            /// owner's savors) and cannot be empty.
            /// </summary>
            public CKTrait ApplicableSavors { get; }

            internal Reference( PackageInstance target, ArtifactDependencyKind kind, CKTrait applicableSavors )
            {
                Target = target;
                DependencyKind = kind;
                ApplicableSavors = applicableSavors;
            }
        }

        internal PackageInstance( in ArtifactInstance instance, in CKTrait savors, IReadOnlyList<Reference> deps )
        {
            ArtifactInstance = instance;
            Savors = savors;
            Dependencies = deps;
        }

        /// <summary>
        /// Gets the artifact instance.
        /// This is the key that identifies the package.
        /// </summary>
        public ArtifactInstance ArtifactInstance { get; }

        /// <summary>
        /// Gets the savors of this package. When not null:
        /// <para>
        /// - this trait can not be the empty one (see <see cref="CKTrait.IsEmpty"/>).
        /// </para>
        /// <para>
        /// - the <see cref="CKTrait.Context"/> can be any context.
        /// </para>
        /// <para>
        /// - <see cref="Reference.ApplicableSavors"/> share the same context and are either
        /// equal to these savors or are a subset of them (and cannot be empty either).
        /// </para>
        /// </summary>
        public CKTrait Savors { get; }

        /// <summary>
        /// Gets the list of the dependencies.
        /// </summary>
        public IReadOnlyList<Reference> Dependencies { get; }

        /// <summary>
        /// Compares this instance to another: <see cref="ArtifactInstance"/> is the key:
        /// order is Type, Name and descending Version.
        /// </summary>
        /// <param name="other">The other instance to compare to. Can be null.</param>
        /// <returns>The negative/zero/positive standard value.</returns>
        public int CompareTo( PackageInstance other ) => other == null ? 1 : ArtifactInstance.CompareTo( other.ArtifactInstance );

        /// <summary>
        /// Implements equality based on <see cref="ArtifactInstance"/>.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns>Returns <see cref="ArtifactInstance"/> equality.</returns>
        public bool Equals( PackageInstance other ) => other == null ? false : ArtifactInstance.Equals( other.ArtifactInstance );

        /// <summary>
        /// Relays to <see cref="ArtifactInstance"/>'s equality.
        /// </summary>
        /// <param name="obj">The other object to compare to.</param>
        /// <returns>True if equal, fals otherwise.</returns>
        public override bool Equals( object obj ) => obj is PackageInstance p ? Equals( p ) : false;

        /// <summary>
        /// Gets the <see cref="ArtifactInstance"/>'s hash code.
        /// </summary>
        /// <returns>The hash.</returns>
        public override int GetHashCode() => ArtifactInstance.GetHashCode();

        /// <summary>
        /// Returns the <see cref="ArtifactInstance"/> string.
        /// </summary>
        /// <returns>The readable key.</returns>
        public override string ToString() => ArtifactInstance.ToString();
    }
}
