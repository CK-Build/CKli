using CK.Core;
using CK.Env.NPM;
using CK.Build;
using System.Collections.Generic;
using CK.Env.NodeSln;

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

        public bool PushLocalArtifacts( IEnvLocalFeed feed, IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts, bool arePublicArtifacts )
        {
            if( !target.HandleArtifactType( NPMClient.NPMType ) ) return true;
            var locals = new List<LocalNPMPackageFile>();
            foreach( var a in artifacts )
            {
                if( target.QualityFilter.Accepts( a.Version.PackageQuality ) )
                {
                    var local = feed.GetNPMPackageFile( m, a.Artifact.Name, a.Version );
                    if( local == null )
                    {
                        m.Error( $"Unable to find local NPM package {a}. File '{local}' not found." );
                        return false;
                    }
                    locals.Add( local );
                }
            }
            return target.PushAsync( m, new NPMArtifactLocalSet( locals, arePublicArtifacts ) ).GetAwaiter().GetResult();
        }

        public void Remove( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts )
        {
            foreach( var n in artifacts )
            {
                if( n.Artifact.Type == NPMClient.NPMType )
                {
                    var f = NodeProjectDependency.CreateTarballPath( feed.PhysicalPath, n.Artifact.Name, n.Version );
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
            /*
For npm, the cache should be managed through "cacache".
==> https://github.com/greggman/npm-cache-rm/tree/master
cacache, that has numerous dependencies. It must be launched
by a node project (or is there a simpler way?).

"yarn cache dir"
C:\Users\olivier.spinelli>yarn cache dir
C:\Users\olivier.spinelli\AppData\Local\Yarn\Cache\v6

In this folder packages are folders with this pattern:
npm-@signature-crs-client-signalr-7.1.1-integrity

"yarn cache dir" can only be called near the root (no .yarn folder should
be above it otherwise it fails).
There seems to be no other way than manually deleting the folder in the cache.
             */
        }
    }
}
