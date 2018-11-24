using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public interface ITestRunMemory
    {
        bool HasBeenTested( IActivityMonitor m, string key );

        void SetTested( IActivityMonitor m, string key );
    }
}
