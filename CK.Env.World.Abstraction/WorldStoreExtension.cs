using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public static bool SetState( this IWorldStore @this, IActivityMonitor m, WorldState state, Action<WorldState> a )
        {
            a( state );
            return @this.SetLocalState( m, state );
        }
    }
}
