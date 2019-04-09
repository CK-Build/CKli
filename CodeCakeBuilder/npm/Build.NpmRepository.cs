using Cake.Core;
using CodeCake.Abstractions;
using CSemVer;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace CodeCake
{
    public partial class Build
    {
        /// <summary>
        /// Useless right now.
        /// </summary>
        class NpmRepository : ArtifactRepository
        {
            /// <summary>
            /// Gets the remote target feeds.
            /// (This is extracted as an independent function to be more easily transformable.)
            /// </summary>
            /// <returns></returns>
            public static IEnumerable<NpmRemoteFeed> GetTargetRemoteFeeds( ICakeContext cake, CheckRepositoryInfo info )
            {
                return new NpmRemoteFeed[]{
                    new VSTSNpmFeed( cake, info, "Signature-Code", "https://pkgs.dev.azure.com/Signature-Code/_packaging/Default/npm/registry/" )};
            }

            public NpmRepository( ICakeContext ctx, CheckRepositoryInfo checkInfo, IEnumerable<string> projectsToPublish ) : base( ctx, checkInfo, projectsToPublish )
            {
            }

            public override IEnumerable<ArtifactFeed> GetLocalFeeds()
            {
                return new ArtifactFeed[] { new NpmLocalFeed( Cake, CheckRepositoryInfo.LocalFeedPath ) };
            }

            public override IEnumerable<ArtifactFeed> GetTargetRemoteFeeds()
            {
                return GetTargetRemoteFeeds( Cake, CheckRepositoryInfo );
            }

            public IEnumerable<PackageJson> PackagesJsonInfo { get; private set; }

            protected override IDictionary<string, ArtifactInstance> ArtifactResolver( IEnumerable<string> projectsToPublish )
            {
                PackagesJsonInfo = projectsToPublish.Select( p =>
                {
                    var output = PackageJson.FromDirectoryPath( p );
                    output.ArtifactInstance = new ArtifactInstance( output.ArtifactInstance.Artifact, CheckRepositoryInfo.Version );
                    return output;
                } );
                return PackagesJsonInfo.ToDictionary( k => k.DirectoryPath, p => p.ArtifactInstance );
            }
        }

        class PackageJson
        {
            PackageJson( string name, string directoryPath, SVersion version, List<NpmDependency> npmDependencies, List<string> scripts )
            {
                ArtifactInstance = new ArtifactInstance( "npm", name, version );
                NpmDependencies = npmDependencies;
                DirectoryPath = directoryPath;
                Scripts = scripts;
            }
            public string Scope
            {
                get
                {
                    string match = Regex.Match( ArtifactInstance.Artifact.Name, @"@+.*\/" ).Value;
                    return match.Substring( 1, match.Length-2);
                }
            }

            public ArtifactInstance ArtifactInstance { get; set; }
            public IReadOnlyCollection<NpmDependency> NpmDependencies { get; }
            public string DirectoryPath;
            public IReadOnlyCollection<string> Scripts { get; }
            public static PackageJson FromDirectoryPath( string path )
            {
                List<NpmDependency> deps = new List<NpmDependency>();
                JObject json = JObject.Parse( File.ReadAllText( Path.Combine( path, "package.json" ) ) );
                if( json.TryGetValue( "dependencies", out JToken dependencies ) && dependencies.HasValues )
                {
                    deps.AddRange( dependencies.Children<JProperty>().Select( p => new NpmDependency( false, p.Value.ToString(), p.Name ) ) );
                }
                if( json.TryGetValue( "devDependencies", out JToken devDependencies ) && dependencies.HasValues )
                {
                    deps.AddRange( devDependencies.Children<JProperty>().Select( p => new NpmDependency( true, p.Value.ToString(), p.Name ) ) );
                }

                SVersion version = SVersion.Parse( json.Value<string>( "version" ) );
                string name = json.Value<string>( "name" );
                List<string> scripts = new List<string>();
                if( json.TryGetValue( "scripts", out JToken scriptsToken ) && scriptsToken.HasValues )
                {
                    scripts.AddRange( scriptsToken.Children<JProperty>().Select( p => p.Name ) );
                }
                return new PackageJson( name, path, version, deps, scripts );
            }

            public async Task ApplyChanges( string path )
            {
                JObject json = JObject.Parse( await File.ReadAllTextAsync( path ) );
                if( NpmDependencies.FirstOrDefault( ( x ) => x.IsDev ) != null )
                {
                    json["devDependencies"] = new JObject( NpmDependencies.Where( ( d ) => d.IsDev ).Select( ( d ) => new JProperty( d.Name, d.VersionOrPath ) ) );
                }
                if( NpmDependencies.FirstOrDefault( ( x ) => !x.IsDev ) != null )
                {
                    json["dependencies"] = new JObject( NpmDependencies.Where( ( d ) => !d.IsDev ).Select( ( d ) => new JProperty( d.Name, d.VersionOrPath ) ) );
                }
                await File.WriteAllTextAsync( path, json.ToString() );
            }

            public class NpmDependency
            {
                public NpmDependency( bool isDev, string versionOrPath, string name )
                {
                    IsDev = isDev;
                    VersionOrPath = versionOrPath;
                    Name = name;
                }
                /// <summary>
                /// <see langword="true"/> if it's a dev depencies
                /// </summary>
                public bool IsDev { get; }
                public string VersionOrPath { get; }
                public string Name { get; }
            }
        }
    }
}
