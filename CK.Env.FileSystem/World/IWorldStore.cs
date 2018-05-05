using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    public interface IWorldStore
    {
        IReadOnlyList<IWorldName> ReadWorlds( IActivityMonitor m );

        XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w );

        IWorldName CreateNew( IActivityMonitor m, string name, string ltsKey, XDocument content );

        bool WriteWorldDescription( IActivityMonitor m, IWorldName w, XDocument content );

        WorldState GetLocalState( IActivityMonitor m, IWorldName w );

        bool SetLocalState( IActivityMonitor m, WorldState state );

    }
}
