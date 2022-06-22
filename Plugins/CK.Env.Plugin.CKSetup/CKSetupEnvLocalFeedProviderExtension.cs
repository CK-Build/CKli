using CK.Core;
using CK.Build;
using CK.Env.CKSetup;
using CK.Env.Plugin;
using CKSetup;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public static class CKSetupEnvLocalFeedProviderExtension
    {

        public static string GetCKSetupStorePath( this IEnvLocalFeedProvider @this, IActivityMonitor m, BuildType buildType )
        {
            if( (buildType & BuildType.IsTargetLocal) != 0 ) return @this.Local.GetCKSetupStorePath();
            if( (buildType & BuildType.IsTargetCI) != 0 ) return @this.CI.GetCKSetupStorePath();
            if( (buildType & BuildType.IsTargetRelease) != 0 ) return @this.Release.GetCKSetupStorePath();
            throw new ArgumentException( nameof( BuildType ) );
        }

        public static bool EnsureCKSetupStores( this IEnvLocalFeedProvider @this, IActivityMonitor m )
        {
            try
            {
                string EnsureStore( IEnvLocalFeed feed, Uri prototypeUrl )
                {
                    string path = feed.PhysicalPath.AppendPart( "CKSetupStore" );
                    if( !System.IO.Directory.Exists( path ) ) System.IO.Directory.CreateDirectory( path );
                    using( var s = LocalStore.OpenOrCreate( m, path ) )
                    {
                        s.PrototypeStoreUrl = prototypeUrl;
                    }
                    return path;
                }
                string masterStore = EnsureStore( @this.Release, Facade.DefaultStoreUrl );
                string developStore = EnsureStore( @this.CI, new Uri( masterStore ) );
                string localStore = EnsureStore( @this.Local, new Uri( developStore ) );
                return true;
            }
            catch( Exception ex )
            {
                m.Error( "While creating local CKSetup stores.", ex );
                return false;
            }
        }

        internal static void RemoveCKSetupComponents( IActivityMonitor m, IEnumerable<ArtifactInstance> instances, string storePath )
        {
            var ckSetupComponents = instances.Where( i => i.Artifact.Type == CKSetupClient.CKSetupType )
                                             .ToDictionary( i => i.Artifact.Name, i => i.Version );
            if( ckSetupComponents.Count > 0 )
            {
                using( var cache = LocalStore.OpenOrCreate( m, storePath ) )
                {
                    cache.RemoveComponents( c => ckSetupComponents.TryGetValue( c.Name, out var v ) && c.Version == v );
                }
            }
        }
    }
}
