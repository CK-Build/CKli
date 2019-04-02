using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates any information that enables access to local artifacts.
    /// Actual implementation depends on the type of the artifact and/or its repository.
    /// </summary>
    public interface IArtifactLocalSet
    {
        /// <summary>
        /// Gets the artifact instances.
        /// </summary>
        IEnumerable<ArtifactInstance> Instances { get; }
    }
}
