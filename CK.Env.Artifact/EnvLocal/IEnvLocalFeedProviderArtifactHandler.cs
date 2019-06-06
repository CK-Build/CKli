using CK.Core;
using System.Collections.Generic;

namespace CK.Env
{
    public interface IEnvLocalFeedProviderArtifactHandler
    {
        /// <summary>
        /// Removes all possible occurrences of a set of artifact instances.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="instance">The artifacts to remove.</param>
        void RemoveFromAllCaches( IActivityMonitor m, IEnumerable<ArtifactInstance> instances );

        /// <summary>
        /// Removes artifact instances from a local feed.
        /// </summary>
        /// <param name="feed">The local feed.</param>
        /// <param name="m">The monitor to use.</param>
        /// <param name="artifacts">The artifacts to remove.</param>
        void Remove( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts );

        /// <summary>
        /// Checks any missing artifact instances in a local feed.
        /// </summary>
        /// <param name="feed">The local feed.</param>
        ///<param name="m">Monitor to use.</param>
        /// <param name="artifacts">Set of expected artifact instances.</param>
        /// <param name="missing">Missing instances collector.</param>
        void CollectMissing( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts, HashSet<ArtifactInstance> missing );

        /// <summary>
        /// Locates all instances of the required artifacts in a local feed and pushes
        /// them to the given target.
        /// The repository target may not handle the required type of artifacts: in
        /// such case, true must be returned and no error should be logged.
        /// </summary>
        /// <param name="feed">The local feed.</param>
        /// <param name="m">The monitor to use.</param>
        /// <param name="target">The repository target.</param>
        /// <param name="artifacts">Set of artifact instances.</param>
        /// <returns>True on success, false on error.</returns>
        bool PushLocalArtifacts( IEnvLocalFeed feed, IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts );

    }
}
