using CK.Core;
using System.Diagnostics;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Defines a project to project dependency (inside the same solution).
    /// </summary>
    public readonly struct ProjectReference
    {
        /// <summary>
        /// Gets the project that owns the reference.
        /// </summary>
        public IProject Owner { get; }

        /// <summary>
        /// Gets the referenced project.
        /// </summary>
        public IProject Target { get; }

        /// <summary>
        /// Gets this reference kind: this is almost always <see cref="ArtifactDependencyKind.Transitive"/>.
        /// </summary>
        public ArtifactDependencyKind Kind { get; }

        /// <summary>
        /// Gets the savors that, when not null, is a subset of the <see cref="IProject.Savors"/> (or all the
        /// project's savors) and cannot be empty.
        /// </summary>
        public CKTrait ApplicableSavors { get; }

        /// <summary>
        /// Gets whether this dependency is applicable only to a subset of the <see cref="IProject.Savors"/>.
        /// </summary>
        public bool IsSavored => ApplicableSavors != null && ApplicableSavors.AtomicTraits.Count < Owner.Savors.AtomicTraits.Count;

        internal ProjectReference( IProject o, IProject t, ArtifactDependencyKind kind, CKTrait applicableSavors )
        {
            Debug.Assert( o != null && o.Savors == null && applicableSavors == null
                            || (o.Savors.Context == applicableSavors.Context
                                && !applicableSavors.IsEmpty
                                && o.Savors.IsSupersetOf( applicableSavors )) );
            Owner = o;
            Target = t;
            Kind = kind;
            ApplicableSavors = applicableSavors;
        }

        /// <summary>
        /// Dumps "Owner -> Target (Kind)" string (with [applicable savors] suffix if <see cref="IsSavored"/>).
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => IsSavored
                                                ? $"{Owner} -> {Target} ({Kind}) [{ApplicableSavors}]"
                                                : $"{Owner} -> {Target} ({Kind})";

    }
}
