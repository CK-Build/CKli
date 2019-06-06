using CK.Core;
using CK.Text;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Defines a simple local folder.
    /// </summary>
    public interface IEnvLocalFeed
    {
        /// <summary>
        /// Gets the physical path of this local feed.
        /// </summary>
        NormalizedPath PhysicalPath { get; }

        /// <summary>
        /// Checks any missing artifact instances in this feed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifacts">Set of expected artifact instances.</param>
        /// <returns>A set of missing artifacts.</returns>
        HashSet<ArtifactInstance> GetMissing( IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts );

        /// <summary>
        /// Removes artifact instances from this feed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifacts">The artifacts to remove.</param>
        void Remove( IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts );

        /// <summary>
        /// Locates all instances of the required artifacts and pushes them to the given target.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="target">The repository target.</param>
        /// <param name="artifacts">Set of artifact instances.</param>
        /// <returns>True on success, false on error.</returns>
        bool PushLocalArtifacts( IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts );

        /// <summary>
        /// Clears all local artifacts.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        bool RemoveAll( IActivityMonitor m );
    }
}
