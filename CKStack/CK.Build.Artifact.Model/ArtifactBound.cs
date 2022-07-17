using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Build
{
    /// <summary>
    /// Associates a <see cref="SVersionBound"/> to an <see cref="Artifact"/>: this defines
    /// a simple version constraint for artifacts.
    /// </summary>
    public readonly struct ArtifactBound
    {
        /// <summary>
        /// Gets the type and name.
        /// </summary>
        public Artifact Name { get; }

        /// <summary>
        /// Gets the version bound.
        /// </summary>
        public SVersionBound Bound { get; }

        /// <summary>
        /// Initializes a new <see cref="ArtifactBound"/>.
        /// </summary>
        /// <param name="name">The artifact type and name. <see cref="Artifact.IsValid"/> must be true otherwise an <see cref="ArgumentException"/> is thrown.</param>
        /// <param name="bound">The version bound.</param>
        public ArtifactBound( in Artifact name, in SVersionBound bound )
        {
            if( !name.IsValid ) throw new ArgumentException( "Artifact name must be valid.", nameof( name ) );
            Name = name;
            Bound = bound;
        }

    }
}
