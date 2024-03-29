using CK.Core;
using CK.Build;
using System.Collections.Generic;

namespace CK.Env
{
    public interface IEnvLocalFeedProviderArtifactHandler
    {
        /// <summary>
        /// Removes all possible occurrences of a set of artifact instances.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="instance">The artifacts to remove.</param>
        void RemoveFromAllCaches( IActivityMonitor monitor, IEnumerable<ArtifactInstance> instances );

        /// <summary>
        /// Removes artifact instances from a local feed.
        /// </summary>
        /// <param name="feed">The local feed.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="artifacts">The artifacts to remove.</param>
        void Remove( IEnvLocalFeed feed, IActivityMonitor monitor, IEnumerable<ArtifactInstance> artifacts );

        /// <summary>
        /// Checks any missing artifact instances in a local feed.
        /// </summary>
        /// <param name="feed">The local feed.</param>
        ///<param name="monitor">Monitor to use.</param>
        /// <param name="artifacts">Set of expected artifact instances.</param>
        /// <param name="missing">Missing instances collector.</param>
        void CollectMissing( IEnvLocalFeed feed, IActivityMonitor monitor, IEnumerable<ArtifactInstance> artifacts, HashSet<ArtifactInstance> missing );

        /// <summary>
        /// Locates all instances of the required artifacts in a local feed and pushes
        /// them to the given target.
        /// The repository target may not handle the required type of artifacts: in
        /// such case, true must be returned and no error should be logged.
        /// </summary>
        /// <param name="feed">The local feed.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="target">The repository target.</param>
        /// <param name="artifacts">Set of artifact instances.</param>
        /// <param name="arePublicArtifacts">
        /// True if the artifacts are public and can be pushed to public repositories (no authentication required to download them).
        /// False to forbid these artifacts to eventually be available from any public repositories.
        /// </param>
        /// <returns>True on success, false on error.</returns>
        bool PushLocalArtifacts( IEnvLocalFeed feed, IActivityMonitor monitor, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts, bool arePublicArtifacts );

    }
}
