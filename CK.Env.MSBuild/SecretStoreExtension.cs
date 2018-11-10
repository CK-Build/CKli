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
        public static string GetCKSetupRemoteStorePushKey( this ISecretKeyStore @this, IActivityMonitor m )
        {
            return @this.GetSecretKey( m, "CKSETUPREMOTESTORE_PUSH_API_KEY", false, "Required to push components to https://cksetup.invenietis.net/." );
        }

        public static string GetSignatureOpenSourceFeedPAT( this ISecretKeyStore @this, IActivityMonitor m )
        {
            return @this.GetSecretKey( m, "AZURE_FEED_PAT", false, "Required to push packages to Signature-OpenSource feed." );
        }
    }
}
