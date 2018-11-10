using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public interface ISecretKeyStore
    {
        string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty, string reason = null );

    }
}
