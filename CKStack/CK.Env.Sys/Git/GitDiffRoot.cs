using CK.Core;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Specifies a set of rooted paths in the repository grouped under a name (typically a project name).
    /// </summary>
    public sealed class GitDiffRoot
    {
        /// <summary>
        /// Initializes a new <see cref="GitDiffRoot"/>.
        /// </summary>
        /// <param name="name">The name (typically a project's name).</param>
        /// <param name="paths">The set of paths.</param>
        public GitDiffRoot( string name, IEnumerable<NormalizedPath> paths )
        {
            Name = name;
            Paths = paths;
        }

        /// <summary>
        /// Gets the name of this root.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the paths covered by this root.
        /// </summary>
        public IEnumerable<NormalizedPath> Paths { get; }
    }
}
