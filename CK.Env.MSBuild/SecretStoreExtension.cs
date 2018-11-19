using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    public static class SecretStoreExtension
    {
        public static (string Key, string Secret) GetCKSetupRemoteStorePushKey( this ISecretKeyStore @this, IActivityMonitor m )
        {
            return ("CKSETUPREMOTESTORE_PUSH_API_KEY", @this.GetSecretKey( m, "CKSETUPREMOTESTORE_PUSH_API_KEY", false, "Required to push components to https://cksetup.invenietis.net/." ));
        }
    }
}