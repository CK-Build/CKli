using CK.Core;
using CK.Text;
using CKSetup;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public static class CKSetupLocalFeedProviderExtension
    {
        public const string CKSetupStoreName = "CKSetupStore";

        public static string GetCKSetupStorePath( this IEnvLocalFeed @this )
        {
            return @this.PhysicalPath.AppendPart( CKSetupStoreName );
        }

        public static string GetCKSetupStorePath( this IEnvLocalFeedProvider @this, IActivityMonitor m, BuildType buildType )
        {
            if( (buildType & BuildType.IsTargetLocal) != 0 ) return @this.Local.GetCKSetupStorePath();
            if( (buildType & BuildType.IsTargetDevelop) != 0 ) return @this.Develop.GetCKSetupStorePath();
            if( (buildType & BuildType.IsTargetRelease) != 0 ) return @this.Master.GetCKSetupStorePath();
            throw new ArgumentException( nameof( BuildType ) );
        }

        public static bool EnsureCKSetupStores( this IEnvLocalFeedProvider @this, IActivityMonitor m )
        {
            try
            {
                string EnsureStore( IEnvLocalFeed feed, Uri prototypeUrl )
                {
                    string path = feed.PhysicalPath.AppendPart( "CKSetupStore" );
                    if( !Directory.Exists( path ) ) Directory.CreateDirectory( path );
                    using( var s = LocalStore.OpenOrCreate( m, path ) )
                    {
                        s.PrototypeStoreUrl = prototypeUrl;
                    }
                    return path;
                }
                string masterStore = EnsureStore( @this.Master, Facade.DefaultStoreUrl );
                string developStore = EnsureStore( @this.Develop, new Uri( masterStore ) );
                string localStore = EnsureStore( @this.Local, new Uri( developStore ) );
                return true;
            }
            catch( Exception ex )
            {
                m.Error( "While creating local CKSetup stores.", ex );
                return false;
            }
        }

    }
}
