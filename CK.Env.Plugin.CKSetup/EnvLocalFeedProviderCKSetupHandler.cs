using CK.Core;
using CK.Env.CKSetup;
using CKSetup;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    public class EnvLocalFeedProviderCKSetupHandler : IEnvLocalFeedProviderArtifactHandler
    {
        public void CollectMissing( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts, HashSet<ArtifactInstance> missing )
        {
            var ckSetup = artifacts.Where( i => i.Artifact.Type == CKSetupClient.CKSetupType )
                                   .Select( a => CKSetupArtifactLocalSet.ToComponentRef( a ) ).ToList();
            if( ckSetup.Count > 0 )
            {
                using( var store = LocalStore.OpenOrCreate( m, feed.GetCKSetupStorePath() ) )
                {
                    foreach( var c in ckSetup )
                    {
                        if( !store.Contains( c.Name, c.TargetFramework, c.Version ) )
                        {
                            missing.Add( CKSetupArtifactLocalSet.FromComponentRef( c ) );
                        }
                    }
                }
            }
        }

        public bool PushLocalArtifacts( IEnvLocalFeed feed, IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts )
        {
            if( !target.HandleArtifactType( CKSetupClient.CKSetupType ) ) return true;
            string localStore = feed.GetCKSetupStorePath();
            return target.PushAsync( m, new CKSetupArtifactLocalSet( artifacts, localStore ) ).GetAwaiter().GetResult();
        }

        public void Remove( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts )
        {
            CKSetupEnvLocalFeedProviderExtension.RemoveCKSetupComponents( m, artifacts, feed.GetCKSetupStorePath() );
        }

        public void RemoveFromAllCaches( IActivityMonitor m, IEnumerable<ArtifactInstance> instances )
        {
            CKSetupEnvLocalFeedProviderExtension.RemoveCKSetupComponents( m, instances, Facade.DefaultStorePath );
        }
    }
}
