using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Build
{
    /// <summary>
    /// Immutable model of a package (packages are installable artifacts).
    /// </summary>
    public partial class PackageInstance : IEquatable<PackageInstance>, IComparable<PackageInstance>, IPackageInstanceInfo
    {
        internal PackageInstance( in ArtifactInstance instance,
                                  CKTrait? savors,
                                  PackageState state,
                                  IReadOnlyList<Reference> deps )
        {
            Key = instance;
            Savors = savors;
            State = state;
            Dependencies = deps;
        }

        /// <summary>
        /// Gets the artifact instance.
        /// This is the key that identifies the package.
        /// </summary>
        public ArtifactInstance Key { get; }

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
        public CKTrait? Savors { get; }

        /// <summary>
        /// Gets the state of this instance.
        /// </summary>
        public PackageState State { get; }

        /// <summary>
        /// Gets the list of the dependencies.
        /// </summary>
        public IReadOnlyList<Reference> Dependencies { get; }

        IEnumerable<(ArtifactInstance Target, SVersionLock Lock, PackageQuality MinQuality, ArtifactDependencyKind Kind, CKTrait? Savors)> IPackageInstanceInfo.Dependencies
            => Dependencies.Select( d => (d.BaseTargetKey, d.Lock, d.MinQuality, d.DependencyKind, d.ApplicableSavors) );

        /// <summary>
        /// Compares this instance to another: <see cref="Key"/> is the key:
        /// order is Type, Name and descending Version.
        /// </summary>
        /// <param name="other">The other instance to compare to. Can be null.</param>
        /// <returns>The negative/zero/positive standard value.</returns>
        public int CompareTo( PackageInstance? other ) => other == null ? 1 : Key.CompareTo( other.Key );

        /// <summary>
        /// Implements equality based on <see cref="Key"/>.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns>Returns <see cref="Key"/> equality.</returns>
        public bool Equals( PackageInstance? other ) => other == null ? false : Key.Equals( other.Key );

        /// <summary>
        /// Relays to <see cref="Key"/>'s equality.
        /// </summary>
        /// <param name="obj">The other object to compare to.</param>
        /// <returns>True if equal, false otherwise.</returns>
        public override bool Equals( object? obj ) => obj is PackageInstance p ? Equals( p ) : false;

        /// <summary>
        /// Gets the <see cref="Key"/>'s hash code.
        /// </summary>
        /// <returns>The hash.</returns>
        public override int GetHashCode() => Key.GetHashCode();

        /// <summary>
        /// Returns the <see cref="Key"/> string.
        /// </summary>
        /// <returns>The readable key.</returns>
        public override string ToString() => Key.ToString();
    }
}
