using CK.Core;
using CK.Text;
using CKli;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Tests.LocalTestHelper
{
    public class TestUniverse : IDisposable
    {
        const string _fakeRemoteFeedName = "FakeRemoteFeed";

        /// <summary>
        /// Name of the Git Folder in a Universe Zip storing all the Worlds.
        /// </summary>
        const string _worldsFolderName = "Worlds";

        /// <summary>
        /// Name of the Git Folder in a Universe Zip storing all the stacks.
        /// </summary>
        const string _stackFolderName = "CKTest-Stack";

        /// <summary>
        /// Name of the directory used to clone all the repositories.
        /// </summary>
        const string _devDirectoryName = "dev";

        const string _ckliMapping = "CKli";

        const string _userLocalDirectoryName = "dev";


        public const string PlaceHolderString = "PLACEHOLDER_CKLI_TESTS";


        private readonly IActivityMonitor _m;

        /// <summary>
        /// Instantiate a new <see cref="TestUniverse"/>.
        /// </summary>
        /// <param name="tempPath">Path of the TestHost.</param>
        /// <param name="userHost">The UserHost instantied with this path.</param>
        TestUniverse( IActivityMonitor m, NormalizedPath tempPath, UserHost userHost )
        {
            _m = m;
            UniversePath = tempPath;
            UserHost = userHost;
        }

        static Dictionary<string, StackConfig> LoadConfig( NormalizedPath ckliMapping )
        {
            if( !Directory.Exists( ckliMapping ) ) return new Dictionary<string, StackConfig>();
            return Directory.EnumerateFiles( ckliMapping, "*.World.xml", SearchOption.AllDirectories ).Where( p => !p.Contains( ".git" ) ).ToDictionary( kS => kS, eS => StackConfig.Create( eS ) );
        }

        /// <summary>
        /// The <see cref="UserHost"/> used to run tests on.
        /// </summary>
        public UserHost UserHost { get; private set; }

        public Dictionary<string, StackConfig> Configs { get; private set; }

        /// <summary>
        /// Temp path of the TestHost.
        /// </summary>
        public NormalizedPath UniversePath { get; }

        /// <summary>
        /// Path to the Folder where all the Worlds are stored.
        /// </summary>
        public NormalizedPath WorldsFolder => UniversePath.AppendPart( _worldsFolderName );

        /// <summary>
        /// Path to the "server" side of the git.
        /// </summary>
        public NormalizedPath StackBareGitPath => UniversePath.AppendPart( _stackFolderName );

        /// <summary>
        /// Path to the Folder used as a Remote Feed.
        /// </summary>
        public NormalizedPath FakeRemoteFeedPath => UniversePath.AppendPart( _fakeRemoteFeedName );

        /// <summary>
        /// Path to the Folder used to clone the Worlds Repositories.
        /// </summary>
        public NormalizedPath DevDirectory => UniversePath.AppendPart( _devDirectoryName );

        public NormalizedPath UserLocalDirectory => UniversePath.AppendPart( _userLocalDirectoryName );

        public NormalizedPath StackGitPath => DevDirectory.AppendPart( _stackFolderName );

        public NormalizedPath CKliMapping => UniversePath.AppendPart( _ckliMapping );

        /// <summary>
        /// Create a <see cref="TestUniverse"/> in a given folder.
        /// </summary>
        /// <param name="path"> The path where the <see cref="TestUniverse"/> will be. The Directory will be deleted when disposed.</param>
        /// <returns></returns>
        public static TestUniverse Create( IActivityMonitor m, NormalizedPath path )
        {
            m.Info( $"Creating TestUniverse from {path}." );
            NormalizedPath ckliPath = path.AppendPart( _ckliMapping );
            if( Directory.Exists( ckliPath ) )
            {
                ReplaceInDirectoriesPaths(
                    ckliPath: ckliPath,
                    oldString: GitWorldStore.StackRepo.CleanPathDirName( PlaceHolderString ),
                    newString: GitWorldStore.StackRepo.CleanPathDirName( path )
                );
            }
            PlaceHolderSwapEverything( m, path, PlaceHolderString, path );
            var userHost = new UserHost( new FakeApplicationLifetime(), ckliPath );
            var output = new TestUniverse( m, path, userHost );
            userHost.Initialize( m );
            userHost.WorldStore.DestroyWorld( m, "CK" );
            userHost.WorldStore.DestroyWorld( m, "CK-Build" );
            return output;
        }

        /// <summary>
        /// Replace in all the Git Repositories the
        /// </summary>
        /// <param name="m"></param>
        /// <param name="oldString"></param>
        /// <param name="newString"></param>
        /// <returns></returns>
        static int SwapAllGitOriginPlaceholders( IActivityMonitor m, NormalizedPath tempPath, string oldString, string newString )
        {
            //someone said "you can do that in one line". So i did.
            using( m.OpenInfo( "Replacing Git Remotes URL." ) )
            {
                return Directory.EnumerateDirectories( tempPath, "*.git", SearchOption.AllDirectories )
                    .Select( s => new Repository( s ) )
                    .Select( s => (s, s.Network.Remotes, s.Network.Remotes.Single()) )
                    .Count( s =>
                    {
                        string oldUri = s.Item3.PushUrl;
                        s.Remotes.Update( s.Item3.Name, a =>
                        {
                            a.Url = s.Item3.Url.Replace( oldString, newString );
                            a.PushUrl = s.Item3.PushUrl.Replace( oldString, newString );
                        } );
                        m.Info( $"Replaced origin of repo {s.s.Info.Path} from {oldUri} to {s.Item3.Url}" );
                        s.s.Dispose();
                        return true;
                    } );
            }
        }

        public static void PlaceHolderSwapEverything( IActivityMonitor m, NormalizedPath tempPath, string oldString, string newString )
        {
            var ckliMapping = tempPath.AppendPart( _ckliMapping );
            var c = LoadConfig( ckliMapping );
            foreach( StackConfig config in c.Select( p => p.Value ) )
            {
                config.PlaceHolderSwap( oldString, newString );
                config.Save();
            }
            int cnt = SwapAllGitOriginPlaceholders( m, tempPath, oldString, newString );
            ReplacePlaceHolderInFile( ckliMapping.AppendPart( "WorldLocalMapping.txt" ), oldString, newString );
            ReplacePlaceHolderInFile( ckliMapping.AppendPart( "Stacks.xml" ), oldString, newString );
        }

        static void ReplacePlaceHolderInFile( string filePath, string oldString, string newString )
        {
            if( !File.Exists( filePath ) ) return;
            File.WriteAllText( filePath, File.ReadAllText( filePath ).Replace( oldString, newString ) );
        }

        static void ReplaceInDirectoriesPaths( NormalizedPath ckliPath, string oldString, string newString )
        {
            foreach( var path in Directory.EnumerateDirectories( ckliPath, "*.*" ).Where( s => s.Contains( oldString ) ) )
            {
                Directory.Move( path, path.Replace( oldString, newString ) );
            }
        }

        public NormalizedPath SnapshotState( string imageName )
        {
            NormalizedPath tempPath = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            FileHelper.DirectoryCopy( UniversePath, tempPath, true ); //Try to escape all handles.
            PlaceHolderSwapEverything( _m, tempPath, UniversePath, PlaceHolderString );
            ReplaceInDirectoriesPaths( tempPath.AppendPart( _ckliMapping ), GitWorldStore.StackRepo.CleanPathDirName( UniversePath ), GitWorldStore.StackRepo.CleanPathDirName( PlaceHolderString ) );
            NormalizedPath output = ImageManager.CacheUniverseFolder.AppendPart( imageName + ".zip" );
            if( File.Exists( output ) ) File.Delete( output );
            Directory.CreateDirectory( ImageManager.CacheUniverseFolder );
            ZipFile.CreateFromDirectory( tempPath, output );
            return output;
        }

        public XDocument CreateWorldXml( IActivityMonitor monitor )
        {
            XElement root = new XElement( "CKTest-Build-World" );
            XDocument xDocument = new XDocument( new XDeclaration( "1.0", "utf-8", "" ), root );

            root.Add
            (
                new XElement( "LoadLibrary", new XAttribute( "Name", "CK.Env.Plugin.Basics" ) ),
                new XElement( "LoadLibrary", new XAttribute( "Name", "CK.Env.Plugin.SolutionDriver" ) ),
                new XElement( "LoadLibrary", new XAttribute( "Name", "CK.Env.Plugin.Appveyor" ) ),
                new XElement( "LoadLibrary", new XAttribute( "Name", "CK.Env.Plugin.GitLab" ) ),
                new XElement( "LoadLibrary", new XAttribute( "Name", "CK.Env.Plugin.NPM" ) ),
                new XElement( "LoadLibrary", new XAttribute( "Name", "CK.Env.Plugin.CKSetup" ) ),
                new XElement( "LoadLibrary", new XAttribute( "Name", "CK.Env.Plugin.NuGet" ) ),
                new XElement( "LoadLibrary", new XAttribute( "Name", "CK.Env.Plugin.Dotnet" ) ),

                new XElement( "SharedHttpClient" ),
                new XElement( "ArtifactCenter" ),
                new XElement( "LocalFeedProvider" ),
                new XElement( "NuGetClient" ),
                new XElement( "NPMClient" ),
                new XElement( "CKSetupClient" ),
                new XElement( "World", new XAttribute( "IsPublic", "True" ) ),

                new XElement( "Artifacts",

                    new XElement( "SourceFeeds",
                        new XElement( "Feed",
                            new XAttribute( "Type", "NuGet" ),
                            new XAttribute( "Name", "NuGet" ),
                            new XAttribute( "Url", "https://api.nuget.org/v3/index.json" ) ),
                        new XElement( "Feed",
                            new XAttribute( "Type", "NuGet" ),
                            new XAttribute( "Name", "Local" ),
                            new XAttribute( "Url", "file://PLACEHOLDER_CKLI_TESTS/FakeRemoteFeed" ) )
                        ),


                new XElement( "TargetRepositories",
                    new XElement( "Repository",
                        new XAttribute( "Type", "NuGetStandard" ),
                        new XAttribute( "Name", "local" ),
                        new XAttribute( "Url", "file://PLACEHOLDER_CKLI_TESTS/FakeRemoteFeed" ),
                        new XAttribute( "CheckName", "NuGet:local" ),
                        new XAttribute( "SecretKeyName", string.Empty ) )
                    )
                ),

                new XElement( "SharedSolutionSpec",
                    new XElement( "ArtifactSources",
                        new XElement( "add", new XAttribute( "Name", "NuGet:NuGet" ) ),
                        new XElement( "add", new XAttribute( "Name", "NuGet:Local" ) )
                    ),
                    new XElement( "ArtifactTargets",
                        new XElement( "add", new XAttribute( "Name", "NuGet:Local" ) )
                    ),
                    new XElement( "ExcludedPlugins",
                        new XElement( "add",
                            new XAttribute( "Type", "CK.Env.Plugin.CodeCakeBuilderCSProjFile, CK.Env.Plugin.Basics" ) )
                    )
                ),

                new XElement( "Folder", new XAttribute( "Name", "CKTest-Build" ) )

             );

            //xDocument.Add( root );
            //Store the file in a temp dir and commit it and push it into StackGitPath. Where to store local ? Don't store it imo.
            //File.WriteAllText( universe.StackGitPath.AppendPart(""), xDocument.ToString() );

            return xDocument;
        }


        public void Dispose()
        {
            UserHost.Dispose();
            FileHelper.RawDeleteLocalDirectory( _m, UniversePath );
        }
    }
}
