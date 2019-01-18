using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public interface IBasicApplicationLifetime
    {
        bool StopRequested( IActivityMonitor m );

        bool CanCancelStopRequest { get; }

        void CancelStopRequest();
    }
}
