using CK.Build;
using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Base implementation of a store for worlds. Local state is handled by
    /// default at this level (this can be overridden): local state file is stored
    /// at the root of the world.
    /// </summary>
    public abstract class WorldStore
    {
        /// <summary>
        /// Initializes a new <see cref="WorldStore"/>.
        /// </summary>
        /// <param name="worldLocalMapping">Required path mapper.</param>
        public WorldStore( IWorldLocalMapping worldLocalMapping )
        {
            WorldLocalMapping = worldLocalMapping ?? throw new ArgumentNullException( nameof( worldLocalMapping ) );
        }

        /// <summary>
        /// Gets the mapper.
        /// </summary>
        public IWorldLocalMapping WorldLocalMapping { get; }

        /// <summary>
        /// Returns all the available worlds with their potential local path (ordered by <see cref="IWorldName.FullName"/>).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The world list. Can return null on error.</returns>
        public abstract IReadOnlyList<IRootedWorldName> ReadWorlds( IActivityMonitor m );

        /// <summary>
        /// Creates a new world in this store from an existing source or returns null if the world
        /// already exists or if an error prevents it to be created.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="source">The source origin of the world. It must exist in the current worlds. See <see cref="IWorldName"/>.</param>
        /// <param name="parallelName">The parallel world name to create. Must not be null or empty. See <see cref="IWorldName"/>.</param>
        /// <param name="content">The initial content. Must not be null.</param>
        /// <returns>The new world or null on error.</returns>
        public IRootedWorldName CreateNewParrallel( IActivityMonitor m, IRootedWorldName source, string parallelName, XDocument content )
        {
            if( source == null ) throw new ArgumentNullException( nameof( source ) );
            if( content == null ) throw new ArgumentNullException( nameof( content ) );
            if( String.IsNullOrWhiteSpace( parallelName ) ) throw new ArgumentNullException( nameof( parallelName ) );
            return DoCreateNewParallel( m, source, parallelName, content );
        }

        /// <summary>
        /// Must create a new world in this store or returns null if the world already exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="source">The source origin of the world. It must exist in the current worlds. See <see cref="IWorldName"/>.</param>
        /// <param name="parallelName">The parallel world name to create. See <see cref="IWorldName"/>.</param>
        /// <param name="content">The initial content.</param>
        /// <returns>The new world or null on error.</returns>
        protected abstract LocalWorldName DoCreateNewParallel( IActivityMonitor m, IRootedWorldName source, string parallelName, XDocument content );

        /// <summary>
        /// Gets the world description of one world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world that must exist in the <see cref="ReadWorlds"/> result.</param>
        /// <returns>The Xml document.</returns>
        public abstract XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w );

        /// <summary>
        /// Updates the world description in this store.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name that must exist in this store.</param>
        /// <param name="content">The updated content. Must not be null or empty.</param>
        /// <returns>True on success, false on error.</returns>
        public abstract bool WriteWorldDescription( IActivityMonitor m, IWorldName w, XDocument content );

        /// <summary>
        /// Gets or creates the <see cref="SharedWorldState"/> for a world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name. It must exist in this store.</param>
        /// <returns>The existing or new world shared state.</returns>
        public abstract SharedWorldState GetOrCreateSharedState( IActivityMonitor m, IWorldName w );

        /// <summary>
        /// Gets or creates the <see cref="LocalWorldState"/> for a world.
        /// Default implementation implements xml state file in the root world directory.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name. It must exist in this store.</param>
        /// <returns>The existing or new world local state.</returns>
        public LocalWorldState GetOrCreateLocalState( IActivityMonitor m, IWorldName w )
        {
            if( w == null ) throw new ArgumentNullException( nameof( w ) );
            var d = GetLocalState( m, w );
            if( d == null ) m.Info( $"Creating new local state for {w.FullName}." );
            return new LocalWorldState( this, w, d );
        }

        /// <summary>
        /// Implementation must return a local folder where any local stuff associated
        /// to the world may be stored.
        /// </summary>
        /// <param name="w">The world name. It must exist in this store.</param>
        /// <returns>A local path to an existing and writable directory.</returns>
        public abstract NormalizedPath GetWorkingLocalFolder( IWorldName w );

        /// <summary>
        /// Gets the local state by reading the file <see cref="ToLocalStateFilePath(IWorldName)"/> if it exists.
        /// Returns null otherwise.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name.</param>
        /// <returns>The Xml document or null.</returns>
        protected virtual XDocument GetLocalState( IActivityMonitor m, IWorldName w )
        {
            var path = ToLocalStateFilePath( w );
            return File.Exists( path ) ? XDocument.Load( path ) : null;
        }

        /// <summary>
        /// Saves an existing world's state.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="state">The updated world state. Must not be null.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SaveState( IActivityMonitor m, BaseWorldState state )
        {
            if( state == null ) throw new ArgumentNullException( nameof( state ) );
            try
            {
                if( state is LocalWorldState )
                {
                    return SaveLocalState( m, state.World, state.XDocument );
                }
                return SaveSharedState( m, state.World, state.XDocument );
            }
            catch( Exception ex )
            {
                m.Error( $"While saving {state.World.FullName} state.", ex );
                return false;
            }
        }

        /// <summary>
        /// Saves the local state xml document in <see cref="ToLocalStateFilePath(IWorldName)"/> file.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name.</param>
        /// <param name="d">The document state.</param>
        /// <returns>True on success, false on error.</returns>
        protected virtual bool SaveLocalState( IActivityMonitor m, IWorldName w, XDocument d )
        {
            d.Save( ToLocalStateFilePath( w ) );
            return true;
        }


        /// <summary>
        /// Must saves the shared state xml document.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name.</param>
        /// <param name="d">The document state.</param>
        /// <returns>True on success, false on error.</returns>
        protected abstract bool SaveSharedState( IActivityMonitor m, IWorldName w, XDocument d );

        /// <summary>
        /// Computes the path of the local state file: it is stored at the root of the world
        /// path (see <see cref="IWorldLocalMapping.GetRootPath(IWorldName)"/>).
        /// </summary>
        /// <param name="w">The world name.</param>
        /// <returns>The path to use to read/write the local state.</returns>
        protected virtual NormalizedPath ToLocalStateFilePath( IWorldName w )
        {
            var p = w is IRootedWorldName r ? r.Root : WorldLocalMapping.GetRootPath( w );
            return p.AppendPart( w.FullName + ".World.State.xml" );
        }
    }
}
