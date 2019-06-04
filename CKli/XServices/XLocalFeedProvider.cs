using CK.Core;
using CK.Env;
using CK.Env.Plugin;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace CKli
{
    public class XLocalFeedProvider : XTypedObject, IEnvLocalFeedProvider
    {
        readonly FileSystem _fs;
        readonly List<IEnvLocalFeedProviderArtifactHandler> _handlers;

        public XLocalFeedProvider(
            Initializer initializer,
            FileSystem fs )
            : base( initializer )
        {
            _fs = fs;
            _fs.ServiceContainer.Add<IEnvLocalFeedProvider>( this );
            initializer.Services.Add<IEnvLocalFeedProvider>( this );
            initializer.Services.Add( this );
            var feedRoot = fs.Root.AppendPart( "LocalFeed" );
            Local = new LocalFeed( this, feedRoot, "Local" );
            CI = new LocalFeed( this, feedRoot, "CI" );
            Release = new LocalFeed( this, feedRoot, "Release" );
            ZeroBuild = new LocalFeed( this, feedRoot, "ZeroBuild" );
            _handlers = new List<IEnvLocalFeedProviderArtifactHandler>();
        }

        class LocalFeed : IEnvLocalFeed
        {
            readonly XLocalFeedProvider _provider;

            internal LocalFeed( XLocalFeedProvider provider, NormalizedPath localFeedFolder, string part )
            {
                _provider = provider;
                PhysicalPath = localFeedFolder.AppendPart( part );
                Directory.CreateDirectory( PhysicalPath );
            }

            public NormalizedPath PhysicalPath { get; }

            public HashSet<ArtifactInstance> GetMissing( IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts )
            {
                var missing = new HashSet<ArtifactInstance>();
                foreach( var h in _provider._handlers )
                {
                    h.CollectMissing( this, m, artifacts, missing );
                }
                return missing;
            }

            public bool PushLocalArtifacts( IActivityMonitor m, IArtifactRepository target, IEnumerable<ArtifactInstance> artifacts, bool arePublicArtifacts )
            {
                bool success = true;
                foreach( var h in _provider._handlers )
                {
                    using( m.OpenTrace( $"Pushing for type handler '{h}'." ) )
                    {
                        if( !h.PushLocalArtifacts( this, m, target, artifacts, arePublicArtifacts ) )
                        {
                            m.CloseGroup( "Failed." );
                            success = false;
                        }
                    }
                }
                return success;
            }

            public void Remove( IActivityMonitor m, IEnumerable<ArtifactInstance> artifacts )
            {
                foreach( var h in _provider._handlers )
                {
                    h.Remove( this, m, artifacts );
                }
            }

            public bool RemoveAll( IActivityMonitor m )
            {
                using( m.OpenInfo( $"Removing '{PhysicalPath}' content." ) )
                {
                    bool success = true;
                    foreach( var d in Directory.EnumerateDirectories( PhysicalPath ) )
                    {
                        FileHelper.RawDeleteLocalDirectory( m, d );
                    }
                    foreach( var f in Directory.EnumerateFiles( PhysicalPath ) )
                    {
                        try
                        {
                            File.Delete( f );
                        }
                        catch( Exception ex )
                        {
                            m.Error( $"While deleting file {f}.", ex );
                            success = false;
                        }
                    }
                    return success;
                }
            }
        }

        public IEnvLocalFeed Local { get; }

        public IEnvLocalFeed CI { get; }

        public IEnvLocalFeed Release { get; }

        public IEnvLocalFeed ZeroBuild { get; }

        public void RemoveFromAllCaches( IActivityMonitor m, IEnumerable<ArtifactInstance> instances )
        {
            foreach( var h in _handlers )
            {
                h.RemoveFromAllCaches( m, instances );
            }
        }

        /// <summary>
        /// Registers a new handler.
        /// </summary>
        /// <param name="handler">New artifact handler.</param>
        public void Register( IEnvLocalFeedProviderArtifactHandler handler )
        {
            if( _handlers.Contains( handler ) ) throw new InvalidOperationException();
            _handlers.Add( handler );
        }
    }
}
