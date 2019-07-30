using CK.Core;
using System;

namespace CK.Env
{
    public static class WorldStoreExtension
    {
        /// <summary>
        /// Helper to impact local state an save it.
        /// </summary>
        /// <param name="this">This store.</param>
        /// <param name="m">The monitor to use.</param>
        /// <param name="state">The state to save.</param>
        /// <param name="a">The state modifier.</param>
        /// <returns>True on succes, false on error.</returns>
        public static bool SetLocalState( this WorldStore @this, IActivityMonitor m, LocalWorldState state, Action<LocalWorldState> a )
        {
            a( state );
            return @this.SaveState( m, state );
        }
    }
}
