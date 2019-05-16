using CK.Core;
using CK.Env;
using CK.Env.NuGet;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public class EnvLocalFeedProviderNuGetHandler : IEnvLocalFeedProviderArtifactHandler
    {
        public void CollectMissing( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts, HashSet<ArtifactInstance> missing )
        {
            foreach( var n in artifacts )
            {
                if( n.Artifact.Type == NuGetClient.NuGetType )
                {
                    if( feed.GetNuGetPackageFile( m, n.Artifact.Name, n.Version ) == null ) missing.Add( n );
                }
            }
        }

        public bool PushLocalArtifacts( IEnvLocalFeed feed, IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts )
        {
            if( !target.HandleArtifactType( NuGetClient.NuGetType ) ) return true;
            var locals = new List<LocalNuGetPackageFile>();
            foreach( var a in artifacts )
            {
                var local = feed.GetNuGetPackageFile( m, a.Artifact.Name, a.Version );
                if( local == null )
                {
                    m.Error( $"Unable to find local package {a} in {feed.PhysicalPath}." );
                    return false;
                }
                locals.Add( local );
            }
            return target.PushAsync( m, new NuGetArtifactLocalSet( locals ) ).GetAwaiter().GetResult();
        }

        public void Remove( IEnvLocalFeed feed, IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts )
        {
            foreach( var n in artifacts )
            {
                if( n.Artifact.Type == NuGetClient.NuGetType )
                {
                    var f = EnvLocalFeedExtension.GetPackagePath( feed.PhysicalPath, n.Artifact.Name, n.Version );
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
            foreach( var n in instances )
            {
                if( n.Artifact.Type == NuGetClient.NuGetType )
                {
                    EnvLocalFeedProviderExtension.DoRemoveFromNuGetCache( m, n.Artifact.Name, n.Version );
                }
            }
        }
    }
}
