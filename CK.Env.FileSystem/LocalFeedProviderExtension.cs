using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public static class LocalFeedProviderExtension
    {
        public const string CKSetupStoreName = "CKSetupStore";

        public static string GetReleaseCKSetupStorePath( this ILocalFeedProvider @this, IActivityMonitor m )
        {
            return Path.Combine( @this.GetReleaseFeedFolder( m ).PhysicalPath, CKSetupStoreName );
        }

        public static string GetCICKSetupStorePath( this ILocalFeedProvider @this, IActivityMonitor m )
        {
            return Path.Combine( @this.GetCIFeedFolder( m ).PhysicalPath, CKSetupStoreName );
        }

        public static string GetLocalCKSetupStorePath( this ILocalFeedProvider @this, IActivityMonitor m )
        {
            return Path.Combine( @this.GetCIFeedFolder( m ).PhysicalPath, CKSetupStoreName );
        }

    }
}
