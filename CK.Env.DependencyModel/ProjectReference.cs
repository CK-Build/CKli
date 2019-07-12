using System;

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
        /// Gets this reference kind: this is almost always <see cref="ProjectDependencyKind.Transitive"/>.
        /// </summary>
        public ProjectDependencyKind Kind { get; }

        internal ProjectReference( IProject o, IProject t, ProjectDependencyKind kind )
        {
            Owner = o ?? throw new ArgumentNullException();
            Target = t;
            Kind = kind;
        }
    }
}
