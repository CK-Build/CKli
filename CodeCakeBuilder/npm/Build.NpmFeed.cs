using System;
using System.Collections.Generic;
using Cake.Common.IO;
using Cake.Npm;
using Cake.Npm.Publish;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Threading.Tasks;
using CSemVer;
using Cake.Core;
using CodeCake.Abstractions;
using SimpleGitVersion;
using Cake.Common.Diagnostics;

namespace CodeCake
{
    public partial class Build
    {
        abstract class NpmFeed : ArtifactFeed
        {
            public NpmFeed( ICakeContext cake )
                : base( cake )
            {
            }

            /// <summary>
            /// Call <see cref="PublishOnePackage(ArtifactInstance)"/> on all the <see cref="ArtifactsToPublish"/>.
            /// </summary>
            /// <param name="releaseDirectory">Directory where the packed npm module can be found</param>
            /// <returns>The awaitable</returns>
            public override Task PushArtifactsAsync( string releaseDirectory )
            {
                foreach( KeyValuePair<string, ArtifactInstance> package in ArtifactsToPublish )
                {
                    PublishOnePackage( package.Value, releaseDirectory );
                }
                return System.Threading.Tasks.Task.CompletedTask;
            }

            protected abstract void PublishOnePackage( ArtifactInstance package, string tarDirectory );

        }

        class NpmLocalFeed : NpmFeed
        {
            readonly string _pathToLocalFeed;

            public override string Name => "NpmLocalFeed";

            public NpmLocalFeed( ICakeContext cake, string pathToLocalFeed ) : base( cake )
            {
                _pathToLocalFeed = pathToLocalFeed;
            }

            protected override void PublishOnePackage( ArtifactInstance artifact, string releaseDirectory )
            {
                string packPath = Path.Combine( releaseDirectory, GetTgzNameOfPackage( artifact ) );
                Cake.MoveFile( packPath, Path.Combine( _pathToLocalFeed, Path.GetFileName( packPath ) ) );
            }

            /// <summary>
            /// Gets the number of packages that exist in the feed.
            /// This is computed by <see cref="InitializePackagesToPublish"/>.
            /// </summary>
            public override Task InitializeArtifactsToPublishAsync( IReadOnlyDictionary<string, ArtifactInstance> allPackagesToPublish )
            {
                ArtifactsToPublish = (IReadOnlyDictionary<string, ArtifactInstance>)allPackagesToPublish;// allPackagesToPublish.Where( p => !File.Exists( System.IO.Path.Combine( _pathToLocalFeed, GetTgzNameOfPackage( p ) ) ) ).ToList();
                ArtifactsAlreadyPublishedCount = allPackagesToPublish.Count() - ArtifactsToPublish.Count();
                return System.Threading.Tasks.Task.CompletedTask;
            }
        }

        abstract class NpmRemoteFeed : NpmFeed
        {
            protected readonly string FeedUri;

            public NpmRemoteFeed( ICakeContext cake, string secretKeyName, string feedUri )
                : base( cake )
            {
                SecretKeyName = secretKeyName;
                FeedUri = feedUri;
            }

            public override string Name => "NpmRemoteFeed";

            public string SecretKeyName { get; }

            public string ResolveAPIKey() => Cake.InteractiveEnvironmentVariable( SecretKeyName );

            protected abstract NpmrcTokenInjector TokenInjector( string projectPath );

            public override Task InitializeArtifactsToPublishAsync( IReadOnlyDictionary<string, ArtifactInstance> allArtifactsToPublish )
            {
                ArtifactsToPublish = allArtifactsToPublish.Where( p =>
                {
                    using( NpmrcTokenInjector tokenInjector = TokenInjector( p.Key ) )
                    {
                        string viewString = Cake.NpmView( p.Value.Artifact.Name, p.Key );
                        if( string.IsNullOrEmpty( viewString ) ) return true;
                        JObject json = JObject.Parse( viewString );
                        if( json.TryGetValue( "versions", out JToken versions ) )
                        {
                            return !((JArray)versions).ToObject<string[]>().Contains( p.Value.Version.ToString() );
                        }
                        return true;
                    }
                } ).ToDictionary( kvp => kvp.Key, kvp => kvp.Value );
                ArtifactsAlreadyPublishedCount = allArtifactsToPublish.Count() - ArtifactsToPublish.Count();
                return System.Threading.Tasks.Task.CompletedTask;
            }

            protected override void PublishOnePackage( ArtifactInstance artifact, string releasesDir )
            {
                List<string> tags = new List<string>();
                var qualities = artifact.Version.PackageQuality.GetLabels();
                tags.AddRange( qualities.Select( q => q.ToString() ) );
                string artifactPath = ArtifactsToPublish.Single( p => p.Value.Artifact.Name == artifact.Artifact.Name ).Key;
                using( NpmrcTokenInjector tokenInjector = NpmrcTokenInjector.VstsPatLogin( FeedUri, ResolveAPIKey(), Path.Combine( artifactPath, ".npmrc" ) ) )
                {
                    string path = Path.GetFullPath( Path.Combine(
                        releasesDir,
                        GetTgzNameOfPackage( artifact ) )
                    );

                    Cake.NpmPublish(
                        new NpmPublishSettings()
                        {
                            Source = path,
                            WorkingDirectory = artifactPath,
                            Tag = tags.First()
                        }
                    );
                    foreach( string tag in tags.Skip( 1 ) )
                    {
                        Cake.Information( $"Adding tag \"{tag}\" to \"{artifact.Artifact.Name}@{artifact.Version}\"..." );
                        // The FromPath is actually required - if executed outside the relevant directory,
                        // it will miss the .npmrc with registry configs.
                        Cake.NpmDistTagAdd( artifact.Artifact.Name, artifact.Version.ToString(), tag, s => s.FromPath( artifactPath ) );
                    }
                }
            }
        }

        /// <summary>
        /// The secret key name is built by <see cref="SignatureVSTSFeed.GetSecretKeyName"/>:
        /// "AZURE_FEED_" + Organization.ToUpperInvariant().Replace( '-', '_' ).Replace( ' ', '_' ) + "_PAT".
        /// </summary>
        /// <param name="organization">Name of the organization.</param>
        /// <param name="feedName">Identifier of the feed in Azure, inside the organization.</param>
        class VSTSNpmFeed : NpmRemoteFeed
        {
            public VSTSNpmFeed( ICakeContext cake, string organization, string feedName )
                : base( cake,
                        SignatureVSTSFeed.GetSecretKeyName( organization ),
                        $"https://pkgs.dev.azure.com/{organization}/_packaging/{feedName}/npm/registry/" )
            {
            }

            protected override NpmrcTokenInjector TokenInjector( string projectPath )
            {
                return NpmrcTokenInjector.VstsPatLogin( FeedUri, ResolveAPIKey(), Path.Combine( projectPath, ".npmrc" ) );
            }
        }
    }
}
