using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Defines a project to project dependency (inside the same solution).
    /// </summary>
    public readonly struct ProjectReference
    {
        /// <summary>
        /// Gets the project that owns the reference.
        /// </summary>
        public DependentProject Owner { get; }

        /// <summary>
        /// Gets the referenced project.
        /// </summary>
        public DependentProject Target { get; }

        /// <summary>
        /// Gets this reference kind: this is almost always <see cref="ProjectDependencyKind.Transitive"/>.
        /// </summary>
        public ProjectDependencyKind Kind { get; }

        internal ProjectReference( DependentProject o, DependentProject t, ProjectDependencyKind kind )
        {
            Owner = o;
            Target = t;
            Kind = kind;
        }
    }
}
