using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Defines common repository information.
    /// Only the name of the repository is required, all other properties depend on
    /// the actual repository type. 
    /// </summary>
    public interface IArtifactRepositoryInfo
    {
        /// <summary>
        /// Gets the unique name of this repository.
        /// It should uniquely identify the repository in any context and may contain type, address, urls, or any information
        /// that helps defining unicity.
        /// <para>
        /// This name depends on the repository type. When described externally in xml, the "CheckName" attribute when it exists
        /// must be exactly this computed name.
        /// </para>
        /// </summary>
        string UniqueArtifactRepositoryName { get; }
    }
}
