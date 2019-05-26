using CK.Core;
using CK.Env;
using CK.Text;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.NuGet
{
    public class NuGetClient : IArtifactRepositoryFactory, IDisposable
    {
        /// <summary>
        /// Exposes the "NuGet" <see cref="ArtifactType"/>. This type of artifact is installable.
        /// </summary>
        public static readonly ArtifactType NuGetType;

        internal static readonly List<Lazy<INuGetResourceProvider>> Providers;

        readonly Dictionary<string, IInternalFeed> _feeds;
        readonly SourcePackageProvider _sourcePackageProvider;
        internal readonly SourceCacheContext SourceCache;

        #region VSS_NUGET_EXTERNAL_FEED_ENDPOINTS

        private static readonly object _secretKeysLock;
        private static readonly Dictionary<string, string> _secretKeys;
        private static bool _initialized;

        internal static void EnsureVSSFeedEndPointCredentials( IActivityMonitor m, string url, string secret )
        {
            lock( _secretKeysLock )
            {
                if( !_secretKeys.ContainsKey( url ) )
                {
                    _secretKeys.Add( url, secret );
                    // The VSS_NUGET_EXTERNAL_FEED_ENDPOINTS is used by Azure Credential Provider to handle authentication
                    // for the feed.
                    StringBuilder b = new StringBuilder( @"{""endpointCredentials"":[" );
                    bool already = false;
                    foreach( var kv in _secretKeys )
                    {
                        if( already ) b.Append( ',' );
                        else already = true;

                        b.Append( @"{""endpoint"":""" ).AppendJSONEscaped( kv.Key ).Append( @"""," )
                            .Append( @"""username"":""Unused"",""password"":""" ).AppendJSONEscaped( kv.Value ).Append( @"""" )
                            .Append( "}" );
                    }
                    b.Append( "]}" );
                    var json = b.ToString();
                    m.Info( $"Updated VSS_NUGET_EXTERNAL_FEED_ENDPOINTS with {_secretKeys.Count} endpoints." );

                    Debug.Assert( Newtonsoft.Json.Linq.JObject.Parse( json )["endpointCredentials"] != null );
                    Environment.SetEnvironmentVariable( "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", json );
                }
            }
        }

        static async Task<IEnumerable<ICredentialProvider>> GetCredentialProvidersAsync( ILogger logger )
        {
            var providers = new List<ICredentialProvider>();
            var securePluginProviders = await new SecurePluginCredentialProviderBuilder( pluginManager: PluginManager.Instance, canShowDialog: false, logger: logger ).BuildAllAsync();
            providers.AddRange( securePluginProviders );
            return providers;
        }

        internal static void Initalize( NuGetLoggerAdapter logger )
        {
            if( !_initialized )
            {
                lock( _secretKeysLock )
                {
                    if( !_initialized )
                    {
                        using( logger.Monitor.OpenInfo( "Installing the Azure Artifact Credential provider (https://github.com/Microsoft/artifacts-credprovider)." ) )
                        {
                            var a = System.Reflection.Assembly.GetExecutingAssembly();
                            using( var r = new StreamReader( a.GetManifestResourceStream( "CK.Env.Artifact.NuGet.Res.InstallCredentialProvider.ps1.txt" ) ) )
                            {
                                var tempPath = Path.GetTempPath();
                                var installer = Guid.NewGuid().ToString() + ".ps1";
                                var installerPath = Path.Combine( tempPath, installer );
                                File.WriteAllText( installerPath, r.ReadToEnd() );
                                ProcessRunner.RunPowerShell(
                                    logger.Monitor,
                                    tempPath,
                                    installer,
                                    new[] { "-AddNetfx" },
                                    Core.LogLevel.Error,
                                    new[] { ("PSExecutionPolicyPreference", "Bypass") } );
                                File.Delete( installerPath );
                            }
                        }
                        var credProviders = new AsyncLazy<IEnumerable<ICredentialProvider>>( async () => await GetCredentialProvidersAsync( logger ) );
                        HttpHandlerResourceV3.CredentialService = new Lazy<ICredentialService>(
                            () => new CredentialService(
                                providers: credProviders,
                                nonInteractive: true,
                                handlesDefaultCredentials: true ) );
                        _initialized = true;
                    }
                }
            }
        }
        #endregion

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

            bool IPackageSourceProvider.IsPackageSourceEnabled( string name ) => true;

            void IPackageSourceProvider.DisablePackageSource( string name )
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

            PackageSource IPackageSourceProvider.GetPackageSourceByName( string name )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

            PackageSource IPackageSourceProvider.GetPackageSourceBySource( string source )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

            void IPackageSourceProvider.RemovePackageSource( string name )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

            void IPackageSourceProvider.EnablePackageSource( string name )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

            void IPackageSourceProvider.AddPackageSource(PackageSource source)
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

            void IPackageSourceProvider.UpdatePackageSource(PackageSource source, bool updateCredentials, bool updateEnabled)
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

        }

        static NuGetClient()
        {
            NuGetType = ArtifactType.Register( "NuGet", true );

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
            _secretKeysLock = new object();
            _secretKeys = new Dictionary<string, string>();
        }


        public NuGetClient( HttpClient httpClient, ISecretKeyStore keyStore )
        {
            HttpClient = httpClient;
            SecretKeyStore = keyStore;
            _feeds = new Dictionary<string, IInternalFeed>();
            var c = new SourceCacheContext() { NoCache = true };
            SourceCache = c.WithRefreshCacheTrue();
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

        IArtifactRepositoryInfo IArtifactRepositoryFactory.CreateInfo( in XElementReader e )
        {
            return NuGetFeedInfo.Create( e, skipMissingType: true );
        }

        IArtifactRepository IArtifactRepositoryFactory.FindOrCreate( IActivityMonitor m, IArtifactRepositoryInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );
            if( !(info is INuGetFeedInfo fInfo) ) return null;
            return FindOrCreate( fInfo );
        }
    }
}
