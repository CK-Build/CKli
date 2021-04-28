using CK.Core;
using CK.Text;
using CodeCake.Abstractions;
using CSemVer;
using Kuinox.TypedCLI.NPM;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CodeCake
{
    public partial class Build
    {
        abstract class NPMFeed : ArtifactFeed
        {
            public NPMFeed( NPMArtifactType t )
                : base( t )
            {
            }

            /// <summary>
            /// Pushes a set of artifacts.
            /// </summary>
            /// <param name="pushes">The instances to push (that necessary target this feed).</param>
            /// <returns>The awaitable.</returns>
            public override async Task<bool> PushAsync( IActivityMonitor m, IEnumerable<ArtifactPush> pushes )
            {
                bool result = true;
                foreach( ArtifactPush artifactPush in pushes )
                {
                    if( !await PublishOnePackageAsync( m, artifactPush ) ) result = false;
                }
                result &= await OnAllArtifactsPushed( m, pushes );
                return result;
            }
            protected abstract Task<bool> PublishOnePackageAsync( IActivityMonitor m, ArtifactPush push );

            /// <summary>
            /// Called once all the packages are pushed.
            /// Does nothing at this level.
            /// </summary>
            /// <param name="pushes">The instances to push (that necessary target this feed).</param>
            /// <returns>The awaitable.</returns>
            protected virtual Task<bool> OnAllArtifactsPushed( IActivityMonitor m, IEnumerable<ArtifactPush> pushes ) => Task.FromResult( true );
        }

        class NPMLocalFeed : NPMFeed
        {
            readonly NormalizedPath _pathToLocalFeed;

            public override string Name => _pathToLocalFeed;

            public NPMLocalFeed( NPMArtifactType t, NormalizedPath pathToLocalFeed )
                : base( t )
            {
                _pathToLocalFeed = pathToLocalFeed;
            }

            public override Task<IEnumerable<ArtifactPush>> CreatePushListAsync( IActivityMonitor m, IEnumerable<ILocalArtifact> artifacts )
            {
                var result = artifacts.Cast<NPMPublishedProject>()
                                .Select( a => (A: a, P: _pathToLocalFeed.AppendPart( a.TGZName )) )
                                .Where( aP => !File.Exists( aP.P ) )
                                .Select( aP => new ArtifactPush( aP.A, this ) )
                                .ToList();
                return System.Threading.Tasks.Task.FromResult<IEnumerable<ArtifactPush>>( result );
            }

            protected override Task<bool> PublishOnePackageAsync( IActivityMonitor m, ArtifactPush p )
            {
                if( p.Feed != this ) throw new ArgumentException( "ArtifactPush: feed mismatch." );
                var project = (NPMPublishedProject)p.LocalArtifact;
                var target = _pathToLocalFeed.AppendPart( project.TGZName );
                var source = ArtifactType.GlobalInfo.ReleasesFolder.AppendPart( project.TGZName );
                File.Copy( source.Path, target.Path );
                return Task.FromResult( true );
            }
        }

        abstract class NPMRemoteFeedBase : NPMFeed
        {
            protected readonly string FeedUri;

            public NPMRemoteFeedBase( NPMArtifactType t, string secretKeyName, string feedUri )
                : base( t )
            {
                SecretKeyName = secretKeyName;
                FeedUri = feedUri;
            }

            public override string Name => FeedUri;

            public string SecretKeyName { get; }

            public string? ResolveAPIKey( IActivityMonitor m )
            {
                string? output = GlobalInfo.InteractiveEnvironmentVariable( m, SecretKeyName );
                if( string.IsNullOrWhiteSpace( output ) )
                {
                    m.Warn( "Missing environement variable " + SecretKeyName );
                }
                return output;
            }

            protected abstract IDisposable TokenInjector( IActivityMonitor m, NPMPublishedProject project );

            public override async Task<IEnumerable<ArtifactPush>> CreatePushListAsync( IActivityMonitor m, IEnumerable<ILocalArtifact> artifacts )
            {
                List<ArtifactPush> result = new();
                foreach( ILocalArtifact item in artifacts )
                {
                    NPMPublishedProject artifact = (NPMPublishedProject)item;
                    using( TokenInjector( m, artifact ) )
                    {
                        string? viewString = await Npm.View( m, thingToView: artifact.Name, workingDirectory: artifact.DirectoryPath, json: true );
                        if( ShouldUpload( artifact, viewString ) ) result.Add( new ArtifactPush( artifact, this ) );
                    }
                }

                bool ShouldUpload( NPMPublishedProject artifact, string? viewString )
                {
                    if( string.IsNullOrEmpty( viewString ) ) return true;
                    JObject json = JObject.Parse( viewString );
                    if( json.TryGetValue( "versions", out JToken? versions ) )
                    {
                        return !((JArray?)versions).ToObject<string[]>().Contains( artifact.ArtifactInstance.Version.ToNormalizedString() );
                    }
                    return true;
                }

                return result;
            }

            protected override async Task<bool> PublishOnePackageAsync( IActivityMonitor m, ArtifactPush p )
            {
                var tags = p.Version.PackageQuality.GetAllQualities().Select( l => l.ToString().ToLowerInvariant() ).ToList();
                var project = (NPMPublishedProject)p.LocalArtifact;
                using( TokenInjector( m, project ) )
                {
                    var absTgzPath = Path.GetFullPath( ArtifactType.GlobalInfo.ReleasesFolder.AppendPart( project.TGZName ) );
                    bool result = await Npm.Publish( m,
                        workingDirectory: project.DirectoryPath,
                        thingsToPublish: new string[] { absTgzPath },
                        tag: tags.First() );
                    if( !result ) return false;
                    using( m.OpenInfo( $"Settings tags '{tags.Concatenate( ", " )} on '{project.Name}' to '{project.ArtifactInstance.Version.ToNormalizedString()}' version." ) )
                    {
                        bool tagSet = await Npm.DistTag.Add( m, project.Name, tags.Skip( 1 ), project.DirectoryPath );
                        if( !tagSet )
                        {
                            m.Warn( "Could not set the package tags !!! Since this is not critical, we keep going." );
                        }
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Feed for a standard NPM server.
        /// </summary>
        class NPMRemoteFeed : NPMRemoteFeedBase
        {
            readonly bool _usePassword;

            public NPMRemoteFeed( NPMArtifactType t, string secretKeyName, string feedUri, bool usePassword )
                : base( t, secretKeyName, feedUri )
            {
                _usePassword = usePassword;
            }

            protected override IDisposable TokenInjector( IActivityMonitor m, NPMPublishedProject project )
            {
                return _usePassword
                        ? project.TemporarySetPushTargetAndPasswordLogin( FeedUri, ResolveAPIKey( m ) )
                        : project.TemporarySetPushTargetAndTokenLogin( FeedUri, ResolveAPIKey( m ) );
            }

        }

        /// <summary>
        /// The secret key name is built by <see cref="SignatureVSTSFeed.GetSecretKeyName"/>:
        /// "AZURE_FEED_" + Organization.ToUpperInvariant().Replace( '-', '_' ).Replace( ' ', '_' ) + "_PAT".
        /// </summary>
        /// <param name="organization">Name of the organization.</param>
        /// <param name="feedName">Identifier of the feed in Azure, inside the organization.</param>
        class AzureNPMFeed : NPMRemoteFeedBase
        {
            /// <summary>
            /// Builds the standardized secret key name from the organization name: this is
            /// the Personal Access Token that allows push packages.
            /// </summary>
            /// <param name="organization">Organization name.</param>
            /// <returns>The secret key name to use.</returns>
            public static string GetSecretKeyName( string organization )
                                    => "AZURE_FEED_" + organization.ToUpperInvariant()
                                                                   .Replace( '-', '_' )
                                                                   .Replace( ' ', '_' )
                                                     + "_PAT";

            public AzureNPMFeed( NPMArtifactType t, string organization, string feedName, string? projectName )
                : base( t,
                        GetSecretKeyName( organization ),
                        projectName != null ?
                          $"https://pkgs.dev.azure.com/{organization}/{projectName}/_packaging/{feedName}/npm/registry/"
                        : $"https://pkgs.dev.azure.com/{organization}/_packaging/{feedName}/npm/registry/" )
            {
                Organization = organization;
                FeedName = feedName;
                ProjectName = projectName;
            }

            public string Organization { get; }

            public string FeedName { get; }

            public string? ProjectName { get; }

            public override string Name => $"AzureNPM:{Organization}/{FeedName}";

            protected override IDisposable TokenInjector( IActivityMonitor m, NPMPublishedProject project )
            {
                return project.TemporarySetPushTargetAndAzurePatLogin( FeedUri, ResolveAPIKey( m ) );
            }

            /// <summary>
            /// Implements Package promotion in @CI, @Exploratory, @Preview, @Latest and @Stable views.
            /// </summary>
            /// <param name="ctx">The Cake context.</param>
            /// <param name="pushes">The set of artifacts to promote.</param>
            /// <returns>The awaitable.</returns>
            protected override async Task<bool> OnAllArtifactsPushed( IActivityMonitor m, IEnumerable<ArtifactPush> pushes )
            {
                var basicAuth = Convert.ToBase64String( Encoding.ASCII.GetBytes( ":" + GlobalInfo.InteractiveEnvironmentVariable( m, SecretKeyName ) ) );
                foreach( var p in pushes )
                {
                    bool isNpm = p.Feed.ArtifactType is NPMArtifactType;
                    string uriProtocol = isNpm ? "npm" : "nuget";
                    foreach( var view in p.Version.PackageQuality.GetAllQualities() )
                    {
                        var url = ProjectName != null ?
                              $"https://pkgs.dev.azure.com/{Organization}/{ProjectName}/_apis/packaging/feeds/{FeedName}/{uriProtocol}/packagesBatch?api-version=5.0-preview.1"
                            : $"https://pkgs.dev.azure.com/{Organization}/_apis/packaging/feeds/{FeedName}/{uriProtocol}/packagesBatch?api-version=5.0-preview.1";
                        using( HttpRequestMessage req = new( HttpMethod.Post, url ) )
                        {
                            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue( "Basic", basicAuth );
                            var body = GetPromotionJSONBody( p.Name, p.Version.ToNormalizedString(), view.ToString(), isNpm );
                            req.Content = new StringContent( body, Encoding.UTF8, "application/json" );
                            using( var message = await StandardGlobalInfo.SharedHttpClient.SendAsync( req ) )
                            {
                                if( message.IsSuccessStatusCode )
                                {
                                    m.Info( $"Package '{p.Name}' promoted to view '@{view}'." );
                                }
                                else
                                {
                                    m.Error( $"Package '{p.Name}' promotion to view '@{view}' failed." );
                                    return false;
                                }
                            }
                        }
                    }
                }
                return true;
            }

            string GetPromotionJSONBody( string packageName, string packageVersion, string viewId, bool npm )
            {
                var bodyFormat = @"{
 ""data"": {
    ""viewId"": ""{viewId}""
  },
  ""operation"": 0,
  ""packages"": [{
    ""id"": ""{packageName}"",
    ""version"": ""{packageVersion}"",
    ""protocolType"": ""{NuGetOrNpm}""
  }]
}";
                return bodyFormat.Replace( "{NuGetOrNpm}", npm ? "Npm" : "NuGet" )
                                 .Replace( "{viewId}", viewId )
                                 .Replace( "{packageName}", packageName )
                                 .Replace( "{packageVersion}", packageVersion );
            }

        }
    }
}
