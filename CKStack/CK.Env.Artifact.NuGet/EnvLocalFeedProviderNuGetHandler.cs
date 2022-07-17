using CK.Core;
using CK.Build;
using CK.Env.NuGet;
using System.Collections.Generic;

namespace CK.Env
{
    public class EnvLocalFeedProviderNuGetHandler : IEnvLocalFeedProviderArtifactHandler
    {
        public void CollectMissing( IEnvLocalFeed feed, IActivityMonitor monitor, IEnumerable<ArtifactInstance> artifacts, HashSet<ArtifactInstance> missing )
        {
            foreach( var n in artifacts )
            {
                if( n.Artifact.Type == NuGetClient.NuGetType )
                {
                    if( feed.GetNuGetPackageFile( monitor, n.Artifact.Name, n.Version ) == null ) missing.Add( n );
                }
            }
        }

        public bool PushLocalArtifacts( IEnvLocalFeed feed, IActivityMonitor monitor, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts, bool arePublicArtifacts )
        {
            if( !target.HandleArtifactType( NuGetClient.NuGetType ) ) return true;
            var locals = new List<LocalNuGetPackageFile>();
            foreach( var a in artifacts )
            {
                var local = feed.GetNuGetPackageFile( monitor, a.Artifact.Name, a.Version );
                if( local == null )
                {
                    monitor.Error( $"Unable to find local package {a} in {feed.PhysicalPath}." );
                    return false;
                }
                locals.Add( local );
            }
            return target.PushAsync( monitor, new NuGetArtifactLocalSet( locals, arePublicArtifacts ) ).GetAwaiter().GetResult();
        }

        public void Remove( IEnvLocalFeed feed, IActivityMonitor monitor, IEnumerable<ArtifactInstance> artifacts )
        {
            foreach( var n in artifacts )
            {
                if( n.Artifact.Type == NuGetClient.NuGetType )
                {
                    var f = EnvLocalFeedExtension.GetPackagePath( feed.PhysicalPath, n.Artifact.Name, n.Version );
                    if( System.IO.File.Exists( f ) )
                    {
                        System.IO.File.Delete( f );
                        monitor.Info( $"Removed {n} from {feed.PhysicalPath}." );
                    }
                }
            }
        }

        public void RemoveFromAllCaches( IActivityMonitor monitor, IEnumerable<ArtifactInstance> instances )
        {
            foreach( var n in instances )
            {
                if( n.Artifact.Type == NuGetClient.NuGetType )
                {
                    EnvLocalFeedProviderExtension.DoRemoveFromNuGetCache( monitor, n.Artifact.Name, n.Version );
                }
            }
        }
    }
}
