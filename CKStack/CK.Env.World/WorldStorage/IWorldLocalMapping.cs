using CK.Core;

using System;

namespace CK.Env
{
    /// <summary>
    /// Enables mapping of a <see cref="IWorldName"/> to its local directory.
    /// </summary>
    public interface IWorldLocalMapping
    {
        /// <summary>
        /// Get the local root directory path for a world.
        /// This root contains the world local state (by default), the "CKli-World.htm"
        /// file marker and the Git repositories.
        /// </summary>
        /// <param name="w">The world name.</param>
        /// <returns>The path to the root directory or <see cref="NormalizedPath.IsEmptyPath"/> if it is not mapped.</returns>
        NormalizedPath GetRootPath( IWorldName w );

        /// <summary>
        /// Gets whether <see cref="SetMap"/> can be called.
        /// </summary>
        bool CanSetMapping { get; }

        /// <summary>
        /// Fires when <see cref="SetMap"/> changed a mapping (and has persisted the change).
        /// </summary>
        event EventHandler? MappingChanged;

        /// <summary>
        /// Creates or updates a mapping between a <see cref="IWorldName.FullName"/> and a local path.
        /// The change is immediately persisted.
        /// <para>
        /// <see cref="CanSetMapping"/> must be true otherwise an <see cref="InvalidOperationException"/> will be thrown.
        /// </para>
        /// </summary>
        /// <param name="m"></param>
        /// <param name="worldFullName">World's full name. Must not be null, empty or white space.</param>
        /// <param name="mappedPath">Local path. Must be rooted.</param>
        /// <returns>True if the path has been set, false if nothing changed.</returns>
        bool SetMap( IActivityMonitor m, string worldFullName, in NormalizedPath mappedPath );

    }
}
