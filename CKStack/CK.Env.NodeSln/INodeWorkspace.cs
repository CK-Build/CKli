using CK.Core;
using System.Collections.Generic;

namespace CK.Env.NodeSln
{

    /// <summary>
    /// Unifies <see cref="AngularWorkspace"/> and <see cref="YarnWorkspace"/>.
    /// </summary>
    public interface INodeWorkspace
    {
        /// <summary>
        /// Gets the workspace path (in the <see cref="FileSystem"/>).
        /// </summary>
        NormalizedPath Path { get; }

        /// <summary>
        /// Gets the projects.
        /// </summary>
        IReadOnlyList<NodeSubProject> Projects { get; }
    }


}


