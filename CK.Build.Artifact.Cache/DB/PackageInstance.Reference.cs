using CK.Core;
using CSemVer;

namespace CK.Build
{
    public partial class PackageInstance
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
    }
}
