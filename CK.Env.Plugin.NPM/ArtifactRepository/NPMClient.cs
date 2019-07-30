using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Xml.Linq;

namespace CK.Env.NPM
{
    public class NPMClient : IArtifactTypeHandler
    {
        /// <summary>
        /// Exposes the "NPM" <see cref="ArtifactType"/>. This type of artifact is installable.
        /// </summary>
        public static readonly ArtifactType NPMType = ArtifactType.Register( "NPM", true );

        public NPMClient( HttpClient httpClient, SecretKeyStore keyStore )
        {
            HttpClient = httpClient;
            SecretKeyStore = keyStore;
        }

        /// <summary>
        /// Gets the key store.
        /// </summary>
        public SecretKeyStore SecretKeyStore { get; }

        /// <summary>
        /// Gets the shared <see cref="HttpClient"/> that will be used for remote access.
        /// </summary>
        public HttpClient HttpClient { get; }

        public IArtifactRepository CreateRepository( in XElementReader r )
        {
            IArtifactRepository result = null;
            var qualityFilter = new PackageQualityFilter( r.HandleOptionalAttribute<string>( "QualityFilter", null ) );
            switch( r.HandleOptionalAttribute<string>( "Type", null ) )
            {
                case "NPMAzure":
                    {
                        var organization = r.HandleRequiredAttribute<string>( "Organization" );
                        var feedName = r.HandleRequiredAttribute<string>( "FeedName" );
                        var npmScope = r.HandleRequiredAttribute<string>( "NPMScope" );
                        if( npmScope.Length <= 2 || npmScope[0] != '@' )
                        {
                            r.ThrowXmlException( $"Invalid NPScope '{npmScope}' (must start with a @)." );
                        }
                        result = new NPMAzureRepository( this, qualityFilter, organization, feedName, npmScope );
                        break;
                    }
                case "NPMStandard":
                    {
                        var name = r.HandleRequiredAttribute<string>( "Name" );
                        var url = r.HandleRequiredAttribute<string>( "Url" );
                        var secretKeyName = r.HandleRequiredAttribute<string>( "SecretKeyName" );
                        var usePassword = r.HandleOptionalAttribute( "UsePassword", false );
                        result = new NPMStandardRepository( this, qualityFilter, name, url, secretKeyName, usePassword );
                        break;
                    }
            }
            if( result != null )
            {
                if( !String.IsNullOrEmpty( result.SecretKeyName ) )
                {
                    SecretKeyStore.DeclareSecretKey( result.SecretKeyName, desc => $"Required to push NPM packages to repository '{result.UniqueRepositoryName}'." );
                }
            }
            return result;
        }

        public IArtifactFeed CreateFeed(
            in XElementReader r,
            IReadOnlyList<IArtifactRepository> repositories,
            IReadOnlyList<IArtifactFeed> feeds )
        {
            if( r.HandleOptionalAttribute<string>( "Type", null ) != NPMType.Name ) return null;
            var url = r.HandleRequiredAttribute<string>( "Url" );
            var scope = r.HandleRequiredAttribute<string>( "Scope" );
            if( !scope.StartsWith( "@" ) ) r.ThrowXmlException( $"Scope attribute must start with @." );
            var xCreds = r.Element.Element( "Credentials" );
            var creds = xCreds != null ? new SimpleCredentials( r.WithElement( xCreds ) ) : null;
            r.WarnUnhandled();

            var npmFeeds = feeds.OfType<NPMFeed>();
            if( npmFeeds.Any( f => f.Scope == scope ) ) r.ThrowXmlException( $"NPM feed with the same scope '{scope}' is already defined." );
            if( npmFeeds.Any( f => StringComparer.OrdinalIgnoreCase.Equals( f.Url, url ) ) ) r.ThrowXmlException( $"NPM feed with the same url '{url}' is already defined." );

            NPMRepositoryBase repository = repositories.OfType<NPMAzureRepository>().FirstOrDefault( repo => repo.Scope == scope );
            if( repository == null )
            {
                repository = repositories.OfType<NPMStandardRepository>().FirstOrDefault( repo => repo.Url.Equals( url, StringComparison.OrdinalIgnoreCase ) );
            }
            return new NPMFeed( this, scope, url, creds, repository );
        }
    }
}
