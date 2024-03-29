using CK.Core;
using CK.Build;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Handles the local feeds for ci builds (from '<see cref="IWorldName.MasterName"/>' branch), releases (from '<see cref="IWorldName.MasterName"/>'
    /// and '<see cref="IWorldName.DevelopName"/>' branches),
    /// local builds (from 'local' branch) and Zero version builds.
    /// </summary>
    public interface IEnvLocalFeedProvider
    {
        /// <summary>
        /// Gets the local feed.
        /// </summary>
        IEnvLocalFeed Local { get; }

        /// <summary>
        /// Gets the CI feed.
        /// CI builds packages come here.
        /// </summary>
        IEnvLocalFeed CI { get; }

        /// <summary>
        /// Gets the Release feed.
        /// </summary>
        IEnvLocalFeed Release { get; }

        /// <summary>
        /// Gets the Zero builds feed.
        /// </summary>
        IEnvLocalFeed ZeroBuild { get; }

        /// <summary>
        /// Removes all possible occurrences of a set of artifact instances.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="instance">The artifacts to remove.</param>
        void RemoveFromAllCaches( IActivityMonitor monitor, IEnumerable<ArtifactInstance> instances );

        /// <summary>
        /// Registers a new local artifact handler.
        /// </summary>
        /// <param name="handler">The local artifact handler to register.</param>
        void Register( IEnvLocalFeedProviderArtifactHandler handler );
    }
}
