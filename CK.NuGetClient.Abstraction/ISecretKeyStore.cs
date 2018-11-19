using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.NuGetClient
{
    public interface ISecretKeyStore
    {
        string GetSecretKey( IActivityMonitor m, string name, bool throwOnEmpty, string message = null );

    }
}
