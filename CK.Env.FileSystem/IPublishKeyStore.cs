using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public interface IPublishKeyStore
    {
        string GetMyGetPushKey( IActivityMonitor m );

        string GetCKSetupRemoteStorePushKey( IActivityMonitor m );

    }
}
