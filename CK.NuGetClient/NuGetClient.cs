using CK.Core;
using CK.Env;
using CK.Text;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.NuGetClient
{
    public class NuGetClient : INuGetClient, IDisposable
    {
        internal static readonly List<Lazy<INuGetResourceProvider>> Providers;

        readonly Dictionary<string, IInternalFeed> _feeds;
        readonly SourcePackageProvider _sourcePackageProvider;
        internal readonly SourceCacheContext SourceCache;

        class SourcePackageProvider : IPackageSourceProvider
        {
            readonly NuGetClient _c;
            readonly List<PackageSource> _packageSources;

            public SourcePackageProvider( NuGetClient c )
            {
                _c = c;
                _packageSources = new List<PackageSource>();
            }

            string IPackageSourceProvider.ActivePackageSourceName => _c._feeds.FirstOrDefault().Key;

            string IPackageSourceProvider.DefaultPushSource => null;

            public event EventHandler PackageSourcesChanged;

            public void RaisePackageSourcesChanged()
            {
                _packageSources.Clear();
                foreach( var f in _c._feeds.Values ) f.CollectPackageSources( _packageSources );
                PackageSourcesChanged?.Invoke( this, EventArgs.Empty );
            }

            public IEnumerable<PackageSource> LoadPackageSources() => _packageSources;

            bool IPackageSourceProvider.IsPackageSourceEnabled( PackageSource source ) => true;

            void IPackageSourceProvider.DisablePackageSource( PackageSource source )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

            void IPackageSourceProvider.SaveActivePackageSource( PackageSource source )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

            void IPackageSourceProvider.SavePackageSources( IEnumerable<PackageSource> sources )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }
        }

        static NuGetClient()
        {
            // Workaround for dev/NuGet.Client\src\NuGet.Core\NuGet.Protocol\Plugins\PluginFactory.cs line 161:
            // FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            // This line should be:
            // FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            //
            // Issue: https://github.com/NuGet/Home/issues/7438
            //
            Environment.SetEnvironmentVariable( "DOTNET_HOST_PATH", "dotnet" );
            Providers = new List<Lazy<INuGetResourceProvider>>();
            Providers.AddRange( Repository.Provider.GetCoreV3() );
        }

        public NuGetClient( HttpClient httpClient, ISecretKeyStore keyStore )
        {
            HttpClient = httpClient;
            SecretKeyStore = keyStore;
            _feeds = new Dictionary<string, IInternalFeed>();
            SourceCache = new SourceCacheContext();
            _sourcePackageProvider = new SourcePackageProvider( this );
        }

        public ISecretKeyStore SecretKeyStore { get; }

        public HttpClient HttpClient { get; }

        public IArtifactRepository Find( string uniqueRepositoryName )
        {
            _feeds.TryGetValue( uniqueRepositoryName, out var f );
            return f;
        }

        public INuGetFeed FindOrCreate( INuGetFeedInfo info )
        {
            if( !_feeds.TryGetValue( info.UniqueArtifactRepositoryName, out var feed ) )
            {
                switch( info )
                {
                    case NuGetAzureFeedInfo a: feed = new NuGetClientAzureFeed( this, a ); break;
                    case NuGetStandardFeedInfo s: feed = new NuGetClientStandardFeed( this, s ); break;
                    default: throw new ArgumentException( $"Unhandled type: {info}", nameof( info ) );
                }
                _feeds.Add( info.UniqueArtifactRepositoryName, feed );
                _sourcePackageProvider.RaisePackageSourcesChanged();
            }
            return feed;
        }

        public void Dispose()
        {
            SourceCache.Dispose();
        }

        IArtifactRepositoryInfo IArtifactTypeFactory.CreateInfo( XElement e )
        {
            return NuGetFeedInfo.Create( e, skipMissingType: true );
        }

        IArtifactRepository IArtifactTypeFactory.FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );
            var fInfo = info as INuGetFeedInfo;
            if( fInfo == null ) return null;
            return FindOrCreate( fInfo );
        }
    }
}
