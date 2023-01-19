using CK.Core;
using System;

namespace CK.Env
{
    public static class WorldStoreExtension
    {
        /// <summary>
        /// Helper to impact local state and save it.
        /// </summary>
        /// <param name="this">This store.</param>
        /// <param name="m">The monitor to use.</param>
        /// <param name="state">The state to save.</param>
        /// <param name="a">The state modifier.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool SetLocalState( this IWorldStore @this, IActivityMonitor m, LocalWorldState state, Action<LocalWorldState> a )
        {
            a( state );
            return @this.SaveState( m, state );
        }

        /// <summary>
        /// Saves an existing world's <see cref="LocalWorldState"/> or <see cref="SharedWorldState"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="state">The updated world state.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool SaveState( this IWorldStore @this, IActivityMonitor m, BaseWorldState state )
        {
            Throw.CheckNotNullArgument( state );
            try
            {
                if( state is LocalWorldState )
                {
                    return @this.SaveLocalState( m, state.World, state.XDocument );
                }
                return @this.SaveSharedState( m, state.World, state.XDocument );
            }
            catch( Exception ex )
            {
                m.Error( $"While saving {state.World.FullName} state.", ex );
                return false;
            }
        }

    }
}
