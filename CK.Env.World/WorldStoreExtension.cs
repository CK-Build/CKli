using CK.Core;
using System;

namespace CK.Env
{
    public static class WorldStoreExtension
    {
        /// <summary>
        /// Helper to impact state an save it.
        /// </summary>
        /// <param name="this">This store.</param>
        /// <param name="m">The monitor to use.</param>
        /// <param name="state">The state to save.</param>
        /// <param name="a">The state modifier.</param>
        /// <returns>True on succes, false on error.</returns>
        public static bool SetState( this IWorldStore @this, IActivityMonitor m, RawXmlWorldState state, Action<RawXmlWorldState> a )
        {
            a( state );
            return @this.SetLocalState( m, state );
        }
    }
}
