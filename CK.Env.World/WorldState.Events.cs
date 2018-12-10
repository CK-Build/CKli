using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Env
{
    public partial class WorldState : IWorldState
    {

        public event EventHandler<EventMonitoredArgs> Initializing;

        public event EventHandler<EventMonitoredArgs> Initialized;

        public event EventHandler<EventMonitoredArgs> SwitchingToLocal;

        public event EventHandler<EventMonitoredArgs> SwitchedToLocal;

        public event EventHandler<EventMonitoredArgs> SwitchingToDevelop;

        public event EventHandler<EventMonitoredArgs> SwitchedToDevelop;

        public event EventHandler<EventMonitoredArgs> ReleaseBuildStarting;

        public event EventHandler<EventMonitoredArgs> ReleaseBuildDone;

    }

}
