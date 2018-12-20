using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Root abstraction that creates repository and repository info of a certain kind.
    /// These handlers are registered in <see cref="ArtifactCenter"/>.
    /// </summary>
    public interface IArtifactTypeFactory
    {
        /// <summary>
        /// Creates a repository information from an Xml element or returns null if the element
        /// cannnot be handled.
        /// </summary>
        /// <param name="e">The xml element. The <see cref="XElement.Name"/> must be ignored (can be anything).</param>
        /// <returns>The repository info or null.</returns>
        IArtifactRepositoryInfo CreateInfo( XElement e );

        /// <summary>
        /// Finds or creates a feed from a feed information if actual information type is handled or null.
        /// Repository unicity (ie. find vs. create) depends on each repository implementation
        /// (see <see cref="IArtifactRepositoryInfo.UniqueArtifactRepositoryName"/>).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="info">The repository information.</param>
        /// <returns>The repository or null.</returns>
        IArtifactRepository FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info );

        /// <summary>
        /// Finds a repository by its <see cref="IArtifactRepositoryInfo.UniqueArtifactRepositoryName"/>.
        /// Null when not found.
        /// </summary>
        /// <param name="uniqueRepositoryName">The unique repository name.</param>
        /// <returns>The repository or null.</returns>
        IArtifactRepository Find( string uniqueRepositoryName );

    }
}
