using CK.Build;
using CK.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace CK.Env
{


    /// <summary>
    /// Base implementation of a store for multiple worlds or single world.
    /// Local state is handled by default at this level (this can be overridden): local state file is stored
    /// at the root of the world.
    /// </summary>
    public abstract class WorldStore : IWorldStore
    {
        protected class SingleWorldMapping : IWorldLocalMapping
        {
            internal SingleWorldMapping( IRootedWorldName w )
            {
                WorldName = w;
            }

            public bool CanSetMapping => false;

            public IRootedWorldName WorldName { get; private set; }

            public event EventHandler? MappingChanged;

            public NormalizedPath GetRootPath( IWorldName w ) => w.FullName == WorldName.FullName ? WorldName.Root : default;

            public bool SetMap( IActivityMonitor m, string worldFullName, in NormalizedPath mappedPath )
            {
                return Throw.InvalidOperationException<bool>( nameof( CanSetMapping ) );
            }

            /// <summary>
            /// Updates the <see cref="WorldName"/> with another object that
            /// must have the same FullName as the current one.
            /// This is mostly an optimization: the object may carry more information than
            /// mere IRootedWorldName.
            /// </summary>
            /// <param name="newName">The new object name.</param>
            public void UpdateSingleName( IRootedWorldName newName )
            {
                if( WorldName.FullName != newName.FullName )
                {
                    throw new ArgumentOutOfRangeException( nameof( newName ) );
                }
                WorldName = newName;
            }
        }

        /// <summary>
        /// Initializes a new <see cref="WorldStore"/> for multiple worlds.
        /// </summary>
        /// <param name="worldLocalMapping">Required path mapper.</param>
        protected WorldStore( IWorldLocalMapping worldLocalMapping )
        {
            Throw.CheckNotNullArgument( worldLocalMapping );
            WorldLocalMapping = worldLocalMapping;
        }

        /// <summary>
        /// Initializes a new <see cref="WorldStore"/> for a single world.
        /// </summary>
        /// <param name="w">The rooted world name.</param>
        protected WorldStore( IRootedWorldName w )
        {
            WorldLocalMapping = SingleWorld = new SingleWorldMapping( w );
        }

        /// <summary>
        /// Gets the mapper that is the <see cref="SingleWorld"/> if this is a single world host.
        /// </summary>
        public IWorldLocalMapping WorldLocalMapping { get; }

        /// <summary>
        /// Gets whether this store manages a single world.
        /// </summary>
        public bool IsSingleWorld => SingleWorld != null;

        /// <summary>
        /// Gets the single mapping if this is a single world store. Null otherwise.
        /// </summary>
        protected SingleWorldMapping? SingleWorld { get; }

        /// <summary>
        /// Returns all the available worlds with their potential local path (ordered by <see cref="IWorldName.FullName"/>).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The world list. Can return null on error.</returns>
        public abstract IReadOnlyList<IRootedWorldName> ReadWorlds( IActivityMonitor m );

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
        public IRootedWorldName? CreateNewParrallel( IActivityMonitor m, IRootedWorldName source, string parallelName, XDocument content )
        {
            Throw.CheckNotNullArgument( source );
            Throw.CheckNotNullArgument( content );
            Throw.CheckNotNullOrWhiteSpaceArgument( parallelName );
            Throw.CheckState( !IsSingleWorld );
            return DoCreateNewParallel( m, source, parallelName, content );
        }

        /// <summary>
        /// Must create a new world in this store or returns null if the world already exists.
        /// This is never called in single World mode.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="source">The source origin of the world. It must exist in the current worlds. See <see cref="IWorldName"/>.</param>
        /// <param name="parallelName">The parallel world name to create. See <see cref="IWorldName"/>.</param>
        /// <param name="content">The initial content.</param>
        /// <returns>The new world or null on error.</returns>
        protected abstract LocalWorldName? DoCreateNewParallel( IActivityMonitor m, IRootedWorldName source, string parallelName, XDocument content );

        public abstract XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w );

        /// <summary>
        /// Updates the world description in this store.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="w">The world name that must exist in this store.</param>
        /// <param name="content">The updated content. Must not be null or empty.</param>
        /// <returns>True on success, false on error.</returns>
        public abstract bool WriteWorldDescription( IActivityMonitor m, IWorldName w, XDocument content );

        public abstract SharedWorldState GetOrCreateSharedState( IActivityMonitor m, IWorldName w );

        public abstract LocalWorldState GetOrCreateLocalState( IActivityMonitor m, IWorldName w );

        public abstract bool SaveLocalState( IActivityMonitor m, IWorldName w, XDocument d );

        public abstract bool SaveSharedState( IActivityMonitor m, IWorldName w, XDocument d );

        public abstract NormalizedPath GetWorkingLocalFolder( IWorldName w );
    }
}
