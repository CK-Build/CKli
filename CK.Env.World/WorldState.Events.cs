using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Env
{
    public partial class WorldState : IWorldState
    {

        public event EventHandler<InitializingEventArgs> Initializing;

        public class InitializingEventArgs : EventMonitoredArgs
        {
            internal readonly List<ISolutionDriver> SolutionsDrivers;

            public InitializingEventArgs( IActivityMonitor m )
                : base( m )
            {
                SolutionsDrivers = new List<ISolutionDriver>();
            }

            public void Register( ISolutionDriver d )
            {
                SolutionsDrivers.Add( d );
            }

        }

        public event EventHandler<EventMonitoredArgs> Initialized;

        public event EventHandler<EventMonitoredArgs> SwitchingToLocal;

        public event EventHandler<EventMonitoredArgs> SwitchedToLocal;

        public event EventHandler<EventMonitoredArgs> SwitchingToDevelop;

        public event EventHandler<EventMonitoredArgs> SwitchedToDevelop;

        public event EventHandler<EventMonitoredArgs> ReleaseBuildStarting;

        public event EventHandler<EventMonitoredArgs> ReleaseBuildDone;

    }

}
