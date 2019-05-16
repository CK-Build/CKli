using CK.Core;
using CK.Env;
using CK.Env.NPM;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public class EnvLocalFeedProviderNPMHandler : IEnvLocalFeedProviderArtifactHandler
    {
        public void CollectMissing( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts, HashSet<ArtifactInstance> missing )
        {
            foreach( var n in artifacts )
            {
                if( n.Artifact.Type == NPMClient.NPMType )
                {
                    if( feed.GetNPMPackageFile( m, n.Artifact.Name, n.Version ) == null ) missing.Add( n );
                }
            }
        }

        public bool PushLocalArtifacts( IEnvLocalFeed feed, IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts )
        {
            if( !target.HandleArtifactType( NPMClient.NPMType ) ) return true;
            var locals = new List<LocalNPMPackageFile>();
            foreach( var a in artifacts )
            {
                var local = feed.GetNPMPackageFile( m, a.Artifact.Name, a.Version );
                if( local == null )
                {
                    m.Error( $"Unable to find local NPM package {a} in {feed.PhysicalPath}." );
                    return false;
                }
                locals.Add( local );
            }
            return target.PushAsync( m, new NPMArtifactLocalSet( locals ) ).GetAwaiter().GetResult();
        }

        public void Remove( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts )
        {
            foreach( var n in artifacts )
            {
                if( n.Artifact.Type == NPMClient.NPMType )
                {
                    var f = NPMEnvLocalFeedProviderExtension.GetNPMPackagePath( feed.PhysicalPath, n.Artifact.Name, n.Version );
                    if( System.IO.File.Exists( f ) )
                    {
                        System.IO.File.Delete( f );
                        m.Info( $"Removed {n} from {feed.PhysicalPath}." );
                    }
                }
            }
        }

        public void RemoveFromAllCaches( IActivityMonitor m, IEnumerable<ArtifactInstance> instances )
        {
            // Once there is a global NPM cache...
        }
    }
}
