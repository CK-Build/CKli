using CK.Core;
using System;

namespace CK.Env
{
    public partial class WorldState : IWorldState
    {
        public event EventHandler<EventMonitoredArgs> DumpWorldStatus;
    }

}
