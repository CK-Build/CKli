using CK.Core;
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

        public static string GetReleaseCKSetupStorePath( this ILocalFeedProvider @this, IActivityMonitor m )
        {
            return Path.Combine( @this.GetReleaseFeedFolder( m ).PhysicalPath, CKSetupStoreName );
        }

        public static string GetDevelopCKSetupStorePath( this ILocalFeedProvider @this, IActivityMonitor m )
        {
            return Path.Combine( @this.GetCIFeedFolder( m ).PhysicalPath, CKSetupStoreName );
        }

        public static string GetLocalCKSetupStorePath( this ILocalFeedProvider @this, IActivityMonitor m )
        {
            return Path.Combine( @this.GetLocalFeedFolder( m ).PhysicalPath, CKSetupStoreName );
        }

        public static string GetCKSetupStorePath( this ILocalFeedProvider @this, IActivityMonitor m, BuildType buildType )
        {
            if( (buildType & BuildType.IsTargetLocal) != 0 ) return @this.GetLocalCKSetupStorePath( m );
            if( (buildType & BuildType.IsTargetDevelop) != 0 ) return @this.GetDevelopCKSetupStorePath( m );
            if( (buildType & BuildType.IsTargetRelease) != 0 ) return @this.GetReleaseCKSetupStorePath( m );
            throw new ArgumentException( nameof( BuildType ) );
        }

        public static bool EnsureCKSetupStores( this ILocalFeedProvider @this, IActivityMonitor m )
        {
            try
            {
                string EnsureStore( IFileInfo folder, Uri prototypeUrl )
                {
                    string path = Path.Combine( folder.PhysicalPath, "CKSetupStore" );
                    if( !Directory.Exists( path ) ) Directory.CreateDirectory( path );
                    using( var s = LocalStore.OpenOrCreate( m, path ) )
                    {
                        s.PrototypeStoreUrl = prototypeUrl;
                    }
                    return path;
                }
                string releaseStore = EnsureStore( @this.GetReleaseFeedFolder( m ), Facade.DefaultStoreUrl );
                string ciStore = EnsureStore( @this.GetCIFeedFolder( m ), new Uri( releaseStore ) );
                string localStore = EnsureStore( @this.GetLocalFeedFolder( m ), new Uri( ciStore ) );
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
