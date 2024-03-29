using CK.Core;
using CK.Build;
using CK.SimpleKeyVault;
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

        public IArtifactRepository CreateRepositoryFromXml( IActivityMonitor monitor, in XElementReader r )
        {
            IArtifactRepository? result = null;
            PackageQualityFilter.TryParse( r.HandleOptionalAttribute<string>( "QualityFilter", String.Empty ), out var qualityFilter );
            switch( r.HandleOptionalAttribute<string>( "Type", null ) )
            {
                case "NPMAzure":
                    {
                        var organization = r.HandleRequiredAttribute<string>( "Organization" );
                        var feedName = r.HandleRequiredAttribute<string>( "FeedName" );
                        var npmScope = r.HandleRequiredAttribute<string>( "NPMScope" );
                        var projectName = r.HandleOptionalAttribute<string>( "ProjectName", null );
                        if( npmScope.Length <= 2 || npmScope[0] != '@' )
                        {
                            r.ThrowXmlException( $"Invalid NPScope '{npmScope}' (must start with a @)." );
                        }
                        result = new NPMAzureRepository( this, qualityFilter, organization, feedName, npmScope, projectName );
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
                    string DescriptionBuilder( SecretKeyInfo? key )
                    {
                        string? desc = key?.Description;
                        string ourDesc = $"Required to push NPM packages to repository '{result.UniqueRepositoryName}'.";
                        return desc != null ? $"{desc}\n{ourDesc}" : ourDesc;
                    }
                    SecretKeyStore.DeclareSecretKey( result.SecretKeyName, DescriptionBuilder );
                }
            }
            return result;
        }

        public IArtifactFeed CreateFeedFromXml(
            IActivityMonitor m,
            in XElementReader r,
            IReadOnlyList<IArtifactRepository> repositories,
            IReadOnlyList<IArtifactFeed> feeds )
        {
            if( r.HandleOptionalAttribute<string>( "Type", null ) != NPMType.Name ) return null;
            // UsePassword: uses basic authentication: this is done by the Registry constructor with 4 parameters.
            // 
            //  string basic = Convert.ToBase64String( Encoding.ASCII.GetBytes( $"{username}:{password}" ) );
            //  _authHeader = new AuthenticationHeaderValue( "Basic", basic );
            //
            var usePassword = r.HandleOptionalAttribute( "UsePassword", false );
            var url = r.HandleRequiredAttribute<string>( "Url" );
            var scope = r.HandleRequiredAttribute<string>( "Scope" );
            if( !scope.StartsWith( "@" ) ) r.ThrowXmlException( $"Scope attribute must start with @." );
            var xCreds = r.Element.Element( "Credentials" );
            var creds = xCreds != null ? new SimpleCredentials( r.WithElement( xCreds, true ) ) : null;
            r.WarnUnhandled();
            var uri = new Uri( url );
            if( uri.Host == "pkgs.dev.azure.com" )
            {
                bool privateFeed = uri.Segments[2] == "_packaging/";
                if( privateFeed && (creds == null) )
                {
                    r.ThrowXmlException( "Detected an azure private feed. Credentials are expected." );
                }
                if( !privateFeed && (creds != null) )
                {
                    r.ThrowXmlException( "Detected an azure public feed. There should be no credentials." );
                }
            }
            var npmFeeds = feeds.OfType<NPMFeed>();
            if( npmFeeds.Any( f => f.Scope == scope ) )
            {
                r.ThrowXmlException( $"NPM feed with the same scope '{scope}' is already defined." );
            }
            if( creds?.IsSecretKeyName ?? false )
            {
                SecretKeyStore.DeclareSecretKey( creds.PasswordOrSecretKeyName, a => "PAT Used to authenticate CKli to the feeds, and retrieve informations about npm packages." );
            }

            return new NPMFeed( scope, url, creds, () =>
            {
                Registry registry = repositories.OfType<NPMAzureRepository>().FirstOrDefault( repo => repo.Scope == scope )?.GetRegistry( m, false )
                                    ?? repositories.OfType<NPMStandardRepository>()
                                                    .FirstOrDefault( repo => repo.Url.Equals( url, StringComparison.OrdinalIgnoreCase ) )?.GetRegistry( m, false );
                if( registry != null ) return registry;

                if( creds == null ) return new Registry( HttpClient, uri );
                string secret = creds.IsSecretKeyName
                                    ? SecretKeyStore.GetSecretKey( m, creds.PasswordOrSecretKeyName, throwOnUnavailable: true )
                                    : creds.PasswordOrSecretKeyName;
                return usePassword
                            ? new Registry( HttpClient, creds.UserName, secret, uri )
                            : new Registry( HttpClient, secret, uri );
            } );
        }
    }
}
