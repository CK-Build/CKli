using CK.Core;
using CK.Build;
using CK.SimpleKeyVault;
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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.NuGet
{
    public class NuGetClient : IArtifactTypeHandler, IDisposable
    {
        /// <summary>
        /// Exposes the "NuGet" <see cref="ArtifactType"/>. This type of artifact is installable.
        /// <see cref="ArtifactType.ContextSavors"/> are used to manage framework names.
        /// The <see cref="CKTraitContext.Separator"/> is the ';' to match the one used by csproj (parsing and
        /// string representation becomes straightforward).
        /// </summary>
        public static readonly ArtifactType NuGetType;

        /// <summary>
        /// Gets the non null NuGet savors traits.
        /// </summary>
        public static CKTraitContext Savors => NuGetType.ContextSavors!;

        internal static readonly List<Lazy<INuGetResourceProvider>> StaticProviders;

        readonly SourcePackageProvider _sourcePackageProvider;
        internal readonly SourceCacheContext SourceCache;

        #region VSS_NUGET_EXTERNAL_FEED_ENDPOINTS

        private static readonly object _secretKeysLock;
        private static readonly Dictionary<string, string> _secretAzureKeys;

        /// <summary>
        /// Method to call to set the credentials of nuget.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="url"></param>
        /// <param name="secret"></param>
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
                }
            }
        }

        class Creds : ICredentialProvider
        {
            public string Id { get; }

            public Task<CredentialResponse> GetAsync(
                Uri uri,
                IWebProxy proxy,
                CredentialRequestType type,
                string message,
                bool isRetry,
                bool nonInteractive,
                CancellationToken cancellationToken ) =>
                Task.FromResult(
                    new CredentialResponse(
                        new NetworkCredential(
                            "CKli",
                                _secretAzureKeys.Single( p => new Uri( p.Key ).ToString() == uri.ToString() ).Value
                        )
                    )
                );
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
            StaticProviders = new List<Lazy<INuGetResourceProvider>>();
            StaticProviders.AddRange( Repository.Provider.GetCoreV3() );
            _secretKeysLock = new object();
            _secretAzureKeys = new Dictionary<string, string>();
            HttpHandlerResourceV3.CredentialService = new Lazy<ICredentialService>(
                            () => new CredentialService(
                                providers: new AsyncLazy<IEnumerable<ICredentialProvider>>(
                                    () => Task.FromResult<IEnumerable<ICredentialProvider>>(
                                        new List<Creds> { new Creds() } )
                                ),
                                nonInteractive: true,
                                handlesDefaultCredentials: true )
                            );
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
            PackageQualityFilter.TryParse( r.HandleOptionalAttribute<string>( "QualityFilter", String.Empty ), out var qualityFilter );
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
                    string DescriptionBuilder( SecretKeyInfo key )
                    {
                        string desc = key?.Description;
                        string ourDesc = $"Required to push NuGet packages to repository '{result.UniqueRepositoryName}'.";
                        return desc != null ? $"{desc}\n{ourDesc}" : ourDesc;
                    }

                    SecretKeyStore.DeclareSecretKey( result.SecretKeyName, DescriptionBuilder );
                }
            }
            return result;
        }

        public IArtifactFeed CreateFeedFromXML( IActivityMonitor m, in XElementReader r, IReadOnlyList<IArtifactRepository> repositories, IReadOnlyList<IArtifactFeed> feeds )
        {
            if( r.HandleOptionalAttribute<string>( "Type", null ) != NuGetType.Name ) return null;
            var url = r.HandleRequiredAttribute<string>( "Url" );
            var name = r.HandleRequiredAttribute<string>( "Name" );
            var xCreds = r.Element.Element( "Credentials" );
            var creds = xCreds != null ? new SimpleCredentials( r.WithElement( xCreds, true ) ) : null;
            var internals = repositories.OfType<NuGetFeedBase>().Concat( feeds.OfType<NuGetFeedBase>() );
            foreach( var i in internals )
            {
                Debug.Assert( i is NuGetRepositoryBase, "We could have looked up for this more precise internal class." );
                if( url.Equals( i.Url, StringComparison.OrdinalIgnoreCase ) )
                {
                    if( i.Feed != null ) r.ThrowXmlException( $"NuGet feed defined by url '{url}' is already registered (by repository {i.Name})." );
                    return i.HandleFeed( SecretKeyStore, url, name, creds );
                }
            }
            var feed = new PureFeed( m, this, url, name, creds );
            _sourcePackageProvider.SetPackageSources( internals.Append( feed ) );
            return feed.Feed;
        }

        /// <summary>
        /// This internal specialization is used for feeds when they don't share the same 
        /// url as an existing Repository.
        /// </summary>
        class PureFeed : NuGetFeedBase
        {
            public PureFeed( IActivityMonitor m, NuGetClient c, string url, string name, SimpleCredentials creds )
                : base( m, c, url, name, creds )
            {
            }

            private protected override bool CanRetry( INuGetResource meta, NuGetLoggerAdapter logger, Exception ex )
            {
                var secretOrName = Feed.Credentials?.PasswordOrSecretKeyName;
                if( String.IsNullOrWhiteSpace( secretOrName ) )
                {
                    logger.Monitor.Trace( "NuGet request failed and there is no Credentials name or password defined. Rethrowing the exception." );
                    return false;
                }
                if( Feed.Credentials.IsSecretKeyName )
                {
                    var secret = Client.SecretKeyStore.GetSecretKey( logger.Monitor, secretOrName, false );
                    if( secret == null )
                    {
                        logger.Monitor.Trace( $"NuGet request failed. No available secret available for {secretOrName}. Rethrowing the exception." );
                        return false;
                    }
                }
                logger.Monitor.Warn( "NuGet request failed but a secret is available. Retrying.", ex );
                return true;
            }
        }

    }
}
