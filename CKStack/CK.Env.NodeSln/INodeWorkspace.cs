using CK.Core;
using System.Collections.Generic;

namespace CK.Env.NodeSln
{
    public interface INodeWorkspace
    {
        /// <summary>
        /// Gets the workspace path (in the <see cref="FileSystem"/>).
        /// </summary>
        NormalizedPath Path { get; }

        IReadOnlyList<NodeSubProject> Projects { get; }
    }


}


