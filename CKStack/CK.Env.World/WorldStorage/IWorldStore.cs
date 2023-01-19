using CK.Core;

using System;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// WIP: this is actually the stack.
    /// Today it manages multiple stack/world. This will be the stack (the stack repository).
    /// </summary>
    public interface IWorldStore
    {
        /// <summary>
        /// Gets whether this store manages a single world.
        /// </summary>
        bool IsSingleWorld { get; }

        /// <summary>
        /// Creates a new world in this store from an existing source or returns null if the world
        /// already exists or if an error prevents it to be created.
        /// <para>
        /// This can be called only if this is a multiple world store otherwise an <see cref="InvalidOperationException"/> is thrown.
        /// </para>
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="source">The source origin of the world. It must exist in the current worlds. See <see cref="IWorldName"/>.</param>
        /// <param name="parallelName">The parallel world name to create. Must not be null or empty. See <see cref="IWorldName"/>.</param>
        /// <param name="content">The initial content. Must not be null.</param>
        /// <returns>The new world or null on error.</returns>
        IRootedWorldName? CreateNewParrallel( IActivityMonitor m, IRootedWorldName source, string parallelName, XDocument content );

        /// <summary>
        /// Gets or creates the <see cref="LocalWorldState"/> for a world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name. It must exist in this store.</param>
        /// <returns>The existing or new world shared state.</returns>
        LocalWorldState GetOrCreateLocalState( IActivityMonitor m, IWorldName w );

        /// <summary>
        /// Gets or creates the <see cref="SharedWorldState"/> for a world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name. It must exist in this store.</param>
        /// <returns>The existing or new world shared state.</returns>
        SharedWorldState GetOrCreateSharedState( IActivityMonitor m, IWorldName w );

        /// <summary>
        /// Saves the local state xml document.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name.</param>
        /// <param name="d">The document state.</param>
        /// <returns>True on success, false on error.</returns>
        bool SaveLocalState( IActivityMonitor m, IWorldName w, XDocument d );

        /// <summary>
        /// Must saves the shared state xml document.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name.</param>
        /// <param name="d">The document state.</param>
        /// <returns>True on success, false on error.</returns>
        bool SaveSharedState( IActivityMonitor m, IWorldName w, XDocument d );

        /// <summary>
        /// Returns the a local folder for the name that can be used to store any
        /// local data.
        /// </summary>
        /// <param name="w">The world name. It must exist in this store.</param>
        /// <returns>A local path to an existing and writable directory.</returns>
        NormalizedPath GetWorkingLocalFolder( IWorldName w );

        /// <summary>
        /// Gets the world description of one world.
        /// This must throw if the document cannot be read or is invalid.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world that must exist.</param>
        /// <returns>The Xml document with a non null <see cref="XDocument.Root"/>.</returns>
        XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w );
    }
}
