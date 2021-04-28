using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;

namespace CK.Build
{
    /// <summary>
    /// Immutable model of a package (packages are installable artifacts).
    /// </summary>
    public class PackageInstance : IEquatable<PackageInstance>, IComparable<PackageInstance>
    {
        /// <summary>
        /// The reference from a <see cref="PackageInstance"/> to a <see cref="Target"/> within a <see cref="VersionBound"/>.
        /// This is a basically the target's <see cref="ArtifactBound"/> with a <see cref="DependencyKind"/> and optionals <see cref="ApplicableSavors"/>.
        /// </summary>
        public readonly struct Reference
        {
            /// <summary>
            /// This target corresponds to the lower bound of this <see cref="VersionBound"/> for this <see cref="Target"/>.
            /// This is used to optimize storage and memory: this value type weights 2 object references and 3 bytes.
            /// </summary>
            readonly PackageInstance _baseTarget;
            readonly CKTrait? _applicableSavors;

            /// <summary>
            /// Gets the target artifact (type and name).
            /// </summary>
            public Artifact Target => _baseTarget.Key.Artifact;

            /// <summary>
            /// Gets the target key (type, name and version) that is the lower bound of
            /// this <see cref="VersionBound"/> for this <see cref="Target"/>.
            /// </summary>
            public ArtifactInstance BaseTargetKey => _baseTarget.Key;

            /// <summary>
            /// Gets the version bound of this reference.
            /// </summary>
            public SVersionBound VersionBound => new SVersionBound( BaseVersion, Lock, MinQuality );

            /// <summary>
            /// Gets the artifact bound of this reference: this contains <see cref="Target"/> (the <see cref="Artifact.Type"/> and <see cref="Artifact.Name"/>),
            /// and the <see cref="VersionBound"/> (with the <see cref="BaseVersion"/>, the <see cref="Lock"/> and <see cref="MinQuality"/>).
            /// </summary>
            public ArtifactBound ArtifactBound => new ArtifactBound( Target, VersionBound );

            /// <summary>
            /// See <see cref="SVersionBound.Base"/>.
            /// </summary>
            public SVersion BaseVersion => _baseTarget.Key.Version;

            /// <summary>
            /// See <see cref="SVersionBound.Lock"/>.
            /// </summary>
            public SVersionLock Lock { get; }

            /// <summary>
            /// See <see cref="SVersionBound.MinQuality"/>.
            /// </summary>
            public PackageQuality MinQuality { get; }

            /// <summary>
            /// Get the kind of dependency to <see cref="Target"/>.
            /// </summary>
            public ArtifactDependencyKind DependencyKind { get; }

            /// <summary>
            /// Gets the savors that, when not null, is a subset of the <see cref="Savors"/> (or all the
            /// owner's savors) and cannot be empty.
            /// </summary>
            public CKTrait? ApplicableSavors => _applicableSavors;

            internal Reference( PackageInstance baseTarget, SVersionLock vL, PackageQuality vQ, ArtifactDependencyKind kind, CKTrait? applicableSavors )
            {
                _baseTarget = baseTarget;
                _applicableSavors = applicableSavors;
                Lock = vL;
                MinQuality = vQ;
                DependencyKind = kind;
            }
        }

        internal PackageInstance(
            in ArtifactInstance instance,
            CKTrait? savors,
            IReadOnlyList<Reference> deps )
        {
            Key = instance;
            Savors = savors;
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
        /// Gets the list of the dependencies.
        /// </summary>
        public IReadOnlyList<Reference> Dependencies { get; }

        /// <summary>
        /// Compares this instance to another: <see cref="Key"/> is the key:
        /// order is Type, Name and descending Version.
        /// </summary>
        /// <param name="other">The other instance to compare to. Can be null.</param>
        /// <returns>The negative/zero/positive standard value.</returns>
        public int CompareTo( PackageInstance other ) => other == null ? 1 : Key.CompareTo( other.Key );

        /// <summary>
        /// Implements equality based on <see cref="Key"/>.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns>Returns <see cref="Key"/> equality.</returns>
        public bool Equals( PackageInstance other ) => other == null ? false : Key.Equals( other.Key );

        /// <summary>
        /// Relays to <see cref="Key"/>'s equality.
        /// </summary>
        /// <param name="obj">The other object to compare to.</param>
        /// <returns>True if equal, fals otherwise.</returns>
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
