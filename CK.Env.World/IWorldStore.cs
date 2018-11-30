using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Store for worlds.
    /// </summary>
    public interface IWorldStore
    {
        /// <summary>
        /// Returns all the available worlds in this store.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The world list.</returns>
        IReadOnlyList<IWorldName> ReadWorlds( IActivityMonitor m );

        /// <summary>
        /// Gets the world description of one world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world that must exist in the <see cref="ReadWorlds"/> result.</param>
        /// <returns>The Xml document.</returns>
        XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w );

        /// <summary>
        /// Creates a new world in this store.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="name">The name of the world. See <see cref="IWorldName"/>.</param>
        /// <param name="ltsKey">The long-term support key. See <see cref="IWorldName"/>.</param>
        /// <param name="content">The initial content. Must not be null.</param>
        /// <returns>The new world or null on error.</returns>
        IWorldName CreateNew( IActivityMonitor m, string name, string ltsKey, XDocument content );

        /// <summary>
        /// Updates the world description to this store.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name that must exist in this store.</param>
        /// <param name="content">The updated content. Must not be null or empty.</param>
        /// <returns>True on success, false on error.</returns>
        bool WriteWorldDescription( IActivityMonitor m, IWorldName w, XDocument content );

        /// <summary>
        /// Gets or creates the local <see cref="RawXmlWorldState"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name. It must exist in this store.</param>
        /// <returns>The existing or new world state</returns>
        RawXmlWorldState GetOrCreateLocalState( IActivityMonitor m, IWorldName w );

        /// <summary>
        /// Upddates the world state of an existing world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="state">The updated world state.</param>
        /// <returns>True on success, false on error.</returns>
        bool SetLocalState( IActivityMonitor m, RawXmlWorldState state );

    }
}
