using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.NuGet
{
    /// <summary>
    /// NuGet repository.
    /// </summary>
    public interface INuGetRepository : IArtifactRepository
    {
        /// <summary>
        /// Gets the name of this repository.
        /// </summary>
        string Name { get; }
    }
}
