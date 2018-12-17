using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// An artifact is produced by a solution and can be of any type.
    /// </summary>
    public readonly struct GeneratedArtifact
    {
        /// <summary>
        /// Gets the artifact type.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Gets the artifact name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a new <see cref="GeneratedArtifact"/>.
        /// </summary>
        /// <param name="type">Artifact type.</param>
        /// <param name="name">Artifact name.</param>
        public GeneratedArtifact( string type, string name )
        {
            Type = type;
            Name = name;
        }
    }
}
