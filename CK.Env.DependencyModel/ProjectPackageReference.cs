using CK.Core;
using CK.Build;
using System;
using System.Diagnostics;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Defines a dependency to a package.
    /// </summary>
    public readonly struct ProjectPackageReference
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
        public ArtifactDependencyKind Kind { get; }

        /// <summary>
        /// Gets the savors that, when not null, is a subset of the <see cref="IProject.Savors"/> (or all of them)
        /// and cannot be empty.
        /// </summary>
        public CKTrait? ApplicableSavors { get; }

        /// <summary>
        /// Gets whether this dependency is applicable only to a subset of the <see cref="IProject.Savors"/>.
        /// </summary>
        public bool IsSavored => ApplicableSavors != null
                                 && ApplicableSavors.AtomicTraits.Count < Owner.Savors!.AtomicTraits.Count;

        internal ProjectPackageReference( IProject o, ArtifactInstance t, ArtifactDependencyKind kind, CKTrait? applicableSavors )
        {
            Debug.Assert( o.Savors == null && applicableSavors == null
                            ||
                        ( o.Savors != null && applicableSavors != null
                          && o.Savors.Context == applicableSavors.Context
                          && !applicableSavors.IsEmpty
                          && o.Savors.IsSupersetOf( applicableSavors )) );
            Owner = o;
            Target = t;
            Kind = kind;
            ApplicableSavors = applicableSavors;
        }

        internal ProjectPackageReference( in ProjectPackageReference o, Func<CKTrait?, CKTrait?> f )
        {
            Owner = o.Owner;
            Target = o.Target;
            Kind = o.Kind;
            ApplicableSavors = f( o.ApplicableSavors );
        }

        /// <summary>
        /// Return "Target (Kind)" string (with [applicable savors] suffix if <see cref="IsSavored"/>).
        /// </summary>
        /// <returns>A readable string.</returns>
        public string ToStringTarget() => IsSavored
                                                ? Kind != ArtifactDependencyKind.Transitive
                                                    ? $"{Target} (RefKind:{Kind}) [{ApplicableSavors}]"
                                                    : $"{Target} [{ApplicableSavors}]"
                                                : (Kind != ArtifactDependencyKind.Transitive
                                                    ? $"{Target} (RefKind:{Kind})"
                                                    : Target.ToString());

        /// <summary>
        /// Returns "Owner -> Target (Kind)" string (with [applicable savors] suffix if <see cref="IsSavored"/>).
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => Owner.ToString() + " -> " + ToStringTarget();
    }
}
