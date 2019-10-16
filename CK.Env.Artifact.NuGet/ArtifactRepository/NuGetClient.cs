using CK.Core;
using CK.Text;
using CSemVer;
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
    public class NuGetClient : IArtifactTypeHandler, IDisposable
    {
        /// <summary>
        /// Exposes the "NuGet" <see cref="ArtifactType"/>. This type of artifact is installable.
        /// Savors are used to manage framework names.
        /// The <see cref="CKTraitContext.Separator"/> is the ';' to match the one used by csproj (parsing and
        /// string representation becomes straightforward).
        /// </summary>
        public static readonly ArtifactType NuGetType;

        internal static readonly List<Lazy<INuGetResourceProvider>> StaticProviders;

        readonly SourcePackageProvider _sourcePackageProvider;
        internal readonly SourceCacheContext SourceCache;

        #region VSS_NUGET_EXTERNAL_FEED_ENDPOINTS

        private static readonly object _secretKeysLock;
        private static readonly Dictionary<string, string> _secretAzureKeys;
        private static bool _initialized;

        internal static void EnsureVSSFeedEndPointCredentials( IActivityMonitor m, string url, string secret )
        {
            lock( _secretKeysLock )
            {
                bool newRepo = !_secretAzureKeys.ContainsKey( url );
                bool updateExisitng = !newRepo && _secretAzureKeys[url] != secret;

                if( newRepo || updateExisitng )
                {
                    if( newRepo )
                    {
                        m.Info( $"Registering credential for '{url}' access." );
                        _secretAzureKeys.Add( url, secret );
                    }
                    else
                    {
                        m.Info( $"Updating credential for '{url}' access." );
                        _secretAzureKeys[url] = secret;
                    }
                    GenerateNuGetCredentialsEnvironmentVariable( m );
                    // Clears the credential service.
                    HttpHandlerResourceV3.CredentialService = null;
                }
            }
        }

        static void GenerateNuGetCredentialsEnvironmentVariable( IActivityMonitor m )
        {
            // The VSS_NUGET_EXTERNAL_FEED_ENDPOINTS is used by Azure Credential Provider to handle authentication
            // for the feed.
            StringBuilder b = new StringBuilder( @"{""endpointCredentials"":[" );
            bool already = false;
            foreach( var kv in _secretAzureKeys )
            {
                if( already ) b.Append( ',' );
                else already = true;

                b.Append( @"{""endpoint"":""" ).AppendJSONEscaped( kv.Key ).Append( @"""," )
                    .Append( @"""username"":""Unused"",""password"":""" ).AppendJSONEscaped( kv.Value ).Append( @"""" )
                    .Append( "}" );
            }
            b.Append( "]}" );
            var json = b.ToString();
            m.Info( $"Updated VSS_NUGET_EXTERNAL_FEED_ENDPOINTS with {_secretAzureKeys.Count} endpoints." );

            Debug.Assert( Newtonsoft.Json.Linq.JObject.Parse( json )["endpointCredentials"] != null );
            Environment.SetEnvironmentVariable( "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", json );
        }

        static async Task<IEnumerable<ICredentialProvider>> GetCredentialProvidersAsync( ILogger logger )
        {
            var providers = new List<ICredentialProvider>();
            var securePluginProviders = await new SecurePluginCredentialProviderBuilder( pluginManager: PluginManager.Instance, canShowDialog: false, logger: logger ).BuildAllAsync();
            providers.AddRange( securePluginProviders );
            return providers;
        }

        internal static bool Initalize( NuGetLoggerAdapter logger )
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
                    _initialized = true;
                }
                if( HttpHandlerResourceV3.CredentialService == null )
                { 
                    var credProviders = new AsyncLazy<IEnumerable<ICredentialProvider>>( async () => await GetCredentialProvidersAsync( logger ) );
                    HttpHandlerResourceV3.CredentialService = new Lazy<ICredentialService>(
                        () => new CredentialService(
                            providers: credProviders,
                            nonInteractive: true,
                            handlesDefaultCredentials: true ) );
                }
            }
            return _initialized;
        }
        #endregion

        class SourcePackageProvider : IPackageSourceProvider
        {
            readonly List<PackageSource> _packageSources;

            public SourcePackageProvider()
            {
                _packageSources = new List<PackageSource>();
            }

            string IPackageSourceProvider.ActivePackageSourceName => _packageSources.FirstOrDefault()?.Name;

            string IPackageSourceProvider.DefaultPushSource => null;

            public event EventHandler PackageSourcesChanged;

            internal void SetPackageSources( IEnumerable<NuGetFeedBase> feeds )
            {
                _packageSources.Clear();
                _packageSources.AddRange( feeds.Select( f => f.PackageSource ) );
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

            void IPackageSourceProvider.AddPackageSource( PackageSource source )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

            void IPackageSourceProvider.UpdatePackageSource( PackageSource source, bool updateCredentials, bool updateEnabled )
            {
                throw new NotSupportedException( "Should not be called in this scenario." );
            }

        }

        static NuGetClient()
        {
            NuGetType = ArtifactType.Register( "NuGet", true, ';' );

            // Workaround for dev/NuGet.Client\src\NuGet.Core\NuGet.Protocol\Plugins\PluginFactory.cs line 161:
            // FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            // This line should be:
            // FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            //
            // Issue: https://github.com/NuGet/Home/issues/7438
            //
            Environment.SetEnvironmentVariable( "DOTNET_HOST_PATH", "dotnet" );
            StaticProviders = new List<Lazy<INuGetResourceProvider>>();
            StaticProviders.AddRange( Repository.Provider.GetCoreV3() );
            _secretKeysLock = new object();
            _secretAzureKeys = new Dictionary<string, string>();
        }

        public NuGetClient( HttpClient httpClient, SecretKeyStore keyStore )
        {
            HttpClient = httpClient;
            SecretKeyStore = keyStore;
            var c = new SourceCacheContext() { NoCache = true };
            SourceCache = c.WithRefreshCacheTrue();
            _sourcePackageProvider = new SourcePackageProvider();
        }

        public SecretKeyStore SecretKeyStore { get; }

        public HttpClient HttpClient { get; }

        public void Dispose()
        {
            SourceCache.Dispose();
        }

        public IArtifactRepository CreateRepository( in XElementReader r )
        {
            IArtifactRepository result = null;
            var qualityFilter = new PackageQualityFilter( r.HandleOptionalAttribute<string>( "QualityFilter", null ) );
            switch( r.HandleOptionalAttribute<string>( "Type", null ) )
            {
                case "NuGetAzure":
                    {
                        var organization = r.HandleRequiredAttribute<string>( "Organization" );
                        var feedName = r.HandleRequiredAttribute<string>( "FeedName" );
                        var projectName = r.HandleOptionalAttribute<string>( "ProjectName", null );
                        var label = r.HandleOptionalAttribute<string>( "Label", null );
                        var name = "Azure:" + organization + '-' + feedName;
                        if( label != null ) name += '-' + label;
                        if( label != null ) label = "@" + label;
                        result = new NuGetAzureRepository( this, name, qualityFilter, organization, feedName, label, projectName );
                        break;
                    }
                case "NuGetStandard":
                    {
                        var name = r.HandleRequiredAttribute<string>( "Name" );
                        var url = r.HandleRequiredAttribute<string>( "Url" );
                        var secretKeyName = r.HandleRequiredAttribute<string>( "SecretKeyName" );
                        result = new NuGetStandardRepository( this, url, name, qualityFilter, secretKeyName );
                        break;
                    }
            }
            if( result != null )
            {
                if( !String.IsNullOrEmpty( result.SecretKeyName ) )
                {
                    SecretKeyStore.DeclareSecretKey( result.SecretKeyName, desc => $"Required to push NuGet packages to repository '{result.UniqueRepositoryName}'." );
                }
            }
            return result;
        }

        public IArtifactFeed CreateFeed( in XElementReader r, IReadOnlyList<IArtifactRepository> repositories, IReadOnlyList<IArtifactFeed> feeds )
        {
            if( r.HandleOptionalAttribute<string>( "Type", null ) != NuGetType.Name ) return null;
            var url = r.HandleRequiredAttribute<string>( "Url" );
            var name = r.HandleRequiredAttribute<string>( "Name" );
            var xCreds = r.Element.Element( "Credentials" );
            var creds = xCreds != null ? new SimpleCredentials( r.WithElement( xCreds, true ) ) : null;
            var uri = new Uri( url );
            if( uri.Host == "pkgs.dev.azure.com" )
            {
                bool privateFeed = uri.Segments[2] == "_packaging/";
                if(privateFeed && (creds == null) )
                {
                    r.ThrowXmlException( "Detected an azure private feed. Credentials are expected." );
                }
                if(!privateFeed && (creds!=null))
                {
                    r.ThrowXmlException( "Detected an azure public feed. There should be no credentials." );
                }
            }
            SecretKeyInfo secretKeyed = null;
            if( creds?.IsSecretKeyName == true )
            {
                secretKeyed = SecretKeyStore.DeclareSecretKey( creds.PasswordOrSecretKeyName, current => current?.Description
                                    ?? $"Required for NuGet.config file to retrieve packages from '{name}' feed." );
            }

            var internals = repositories.OfType<NuGetFeedBase>().Concat( feeds.OfType<NuGetFeedBase>() );
            foreach( var i in internals )
            {
                if( url.Equals( i.Url, StringComparison.OrdinalIgnoreCase ) )
                {
                    if( i.Feed != null ) r.ThrowXmlException( $"NuGet feed defined by url '{url}' is already registered." );
                    return i.HandleFeed( url, name, creds );
                }
            }
            var feed = new PureFeed( this, url, name, creds );
            _sourcePackageProvider.SetPackageSources( internals.Append( feed ) );
            return feed.Feed;
        }

        class PureFeed : NuGetFeedBase
        {
            public PureFeed( NuGetClient c, string url, string name, SimpleCredentials creds )
                : base( c, url, name, creds )
            {
            }

            private protected override bool CanRetry( MetadataResource meta, NuGetLoggerAdapter logger, Exception ex )
            {
                var secretOrName = Feed.Credentials?.PasswordOrSecretKeyName;
                if( String.IsNullOrWhiteSpace( secretOrName ) )
                {
                    logger.Monitor.Trace( "NuGet request failed and there is no Credentials name or password defined. Rethrowing the exception." );
                    return false;                   
                }
                string secret;
                if( Feed.Credentials.IsSecretKeyName )
                {
                    secret = Client.SecretKeyStore.GetSecretKey( logger.Monitor, secretOrName, false );
                    if( secret == null )
                    {
                        logger.Monitor.Trace( $"NuGet request failed. No available secret available for {secretOrName}. Rethrowing the exception." );
                        return false;
                    }
                }
                else secret = secretOrName;
                EnsureVSSFeedEndPointCredentials( logger.Monitor, Url, secret );
                logger.Monitor.Warn( "NuGet request failed but a secret is available. Retrying.", ex );
                return true;
            }
        }

    }
}
