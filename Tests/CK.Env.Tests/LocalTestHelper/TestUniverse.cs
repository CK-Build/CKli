using CK.Core;
using CK.Text;
using CKli;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static CK.Testing.MonitorTestHelper;
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
        /// Instanciates a new <see cref="TestUniverse"/>.
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
            //if( Directory.Exists( ckliPath ) )
            //{
            //    RenameDirectories(
            //        dirPath: ckliPath,
            //        oldString: GitWorldStore.StackRepo.CleanPathDirName( PlaceHolderString ),
            //        newString: GitWorldStore.StackRepo.CleanPathDirName( path )
            //    );
            //}
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
            ChangeStringInAllSubPathAndFileContent( m, tempPath, oldString, newString );
            //var ckliMapping = tempPath.AppendPart( _ckliMapping );
            //var c = LoadConfig( ckliMapping );
            //foreach( StackConfig config in c.Select( p => p.Value ) )
            //{
            //    config.PlaceHolderSwap( oldString, newString );
            //    config.Save();
            //}
            //int cnt = SwapAllGitOriginPlaceholders( m, tempPath, oldString, newString );
            //ReplacePlaceHolderInFile( ckliMapping.AppendPart( "WorldLocalMapping.txt" ), oldString, newString );
            //ReplacePlaceHolderInFile( ckliMapping.AppendPart( "Stacks.xml" ), oldString, newString );
        }

        static void ReplacePlaceHolderInFile( IActivityMonitor m, string filePath, string oldString, string newString )
        {
            string fileContent = File.ReadAllText( filePath );
            if( !fileContent.Contains( oldString ) ) return;
            m.Info( $"Corrected '{oldString}' to '{newString}' in '{filePath}'.'" );
            File.WriteAllText( filePath, fileContent.Replace( oldString, newString ) );
        }

        static void RenameFile( IActivityMonitor m, NormalizedPath filePath, string oldString, string newString )
        {
            if( !filePath.LastPart.Contains( oldString ) ) return;
            NormalizedPath newPath = filePath.RemoveLastPart().AppendPart( filePath.LastPart.Replace( oldString, newString ) );
            m.Info( $"'{filePath}' is now '{newPath}'" );
            File.Move( filePath, newPath );
        }

        static void RenameFolder( IActivityMonitor m, NormalizedPath dirPath, string oldString, string newString )
        {
            oldString = GitWorldStore.StackRepo.CleanPathDirName( oldString );
            newString = GitWorldStore.StackRepo.CleanPathDirName( newString );
            if( !dirPath.LastPart.Contains( oldString ) ) return;
            NormalizedPath newPath = dirPath.RemoveLastPart().AppendPart( dirPath.LastPart.Replace( oldString, newString ) );
            m.Info( $"'{dirPath}' is now '{newPath}'" );
            Directory.Move( dirPath, newPath );
        }

        static void ChangeStringInAllSubPathAndFileContent( IActivityMonitor m, NormalizedPath dirPath, string oldString, string newString )
        {
            //The Ministry of Truth Function
            string[] dirs = Directory.GetDirectories( dirPath );
            for( int i = 0; i < dirs.Length; i++ )
            {
                ChangeStringInAllSubPathAndFileContent( m, dirs[i], oldString, newString );
                RenameFolder( m, dirs[i], oldString, newString );
            }
            string[] files = Directory.GetFiles( dirPath );
            for( int i = 0; i < files.Length; i++ )
            {
                RenameFile( m, files[i], oldString, newString );
                ReplacePlaceHolderInFile( m, files[i], oldString, newString );
            }
        }

        //static void RenameDirectories( NormalizedPath dirPath, string oldString, string newString )
        //{
        //    foreach( var path in Directory.EnumerateDirectories( dirPath, "*.*" ).Where( s => s.Contains( oldString ) ) )
        //    {
        //        Directory.Move( path, path.Replace( oldString, newString ) );
        //    }
        //}

        public NormalizedPath SnapshotState( string imageName )
        {
            NormalizedPath tempPath = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            FileHelper.DirectoryCopy( UniversePath, tempPath, true ); //Try to escape all write handles.
            NormalizedPath output = ImageManager.CacheUniverseFolder.AppendPart( imageName + ".zip" );
            if( File.Exists( output ) ) File.Delete( output );
            Directory.CreateDirectory( ImageManager.CacheUniverseFolder );
            ZipFile.CreateFromDirectory( tempPath, output );
            FileHelper.RawDeleteLocalDirectory( TestHelper.Monitor, tempPath );
            return output;
        }

        public void Dispose()
        {
            UserHost.Dispose();
            FileHelper.RawDeleteLocalDirectory( _m, UniversePath );
        }
    }
}
