using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates any information that enables concrete access to artifacts.
    /// Actual implementation depends on the type of the artifact and/or its repository.
    /// </summary>
    public interface IArtifactLocator
    {
        /// <summary>
        /// Gets the artifact instance.
        /// </summary>
        ArtifactInstance Instance { get; }
    }
}
