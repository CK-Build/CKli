using CK.Core;
using CK.Text;
using CKli;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

using static CK.Testing.BasicTestHelper;

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
        private readonly IActivityMonitor _m;

        /// <summary>
        /// Instantiate a new <see cref="TestUniverse"/>.
        /// </summary>
        /// <param name="tempPath">Path of the TestHost.</param>
        /// <param name="userHost">The UserHost instantied with this path.</param>
        /// <param name="imageName">Name of the image this <see cref="TestUniverse"/> is based on.</param>
        /// <param name="configs">The stacks configs.</param>
        TestUniverse( IActivityMonitor m, NormalizedPath tempPath, UserHost userHost, string imageName )
        {
            _m = m;
            TempPath = tempPath;
            UserHost = userHost;
            ImageName = imageName;
        }

        static Dictionary<string, StackConfig> LoadConfig( NormalizedPath ckliMapping )
        {
            if( !Directory.Exists( ckliMapping ) ) return new Dictionary<string, StackConfig>();
            return Directory.EnumerateFiles( ckliMapping, "*.World.xml", SearchOption.AllDirectories ).Where( p => !p.Contains( ".git" ) ).ToDictionary( kS => kS, eS => StackConfig.Create( eS ) );
        }

        /// <summary>
        /// The <see cref="UserHost"/> used to run tests on.
        /// </summary>
        public UserHost UserHost { get; }

        public Dictionary<string, StackConfig> Configs { get; private set; }

        /// <summary>
        /// Temp path of the TestHost.
        /// </summary>
        public NormalizedPath TempPath { get; }

        public string ImageName { get; }

        /// <summary>
        /// Path to the Folder where all the Worlds are stored.
        /// </summary>
        NormalizedPath WorldsFolder => TempPath.AppendPart( _worldsFolderName );

        /// <summary>
        /// Path to the "server" side of the git.
        /// </summary>
        public NormalizedPath StackBareGitPath => TempPath.AppendPart( _stackFolderName );

        /// <summary>
        /// Path to the Folder used as a Remote Feed.
        /// </summary>
        NormalizedPath FakeRemoteFeedPath => TempPath.AppendPart( _fakeRemoteFeedName );

        /// <summary>
        /// Path to the Folder used to clone the Worlds Repositories.
        /// </summary>
        public NormalizedPath DevDirectory => TempPath.AppendPart( _devDirectoryName );

        public NormalizedPath UserLocalDirectory => TempPath.AppendPart( _userLocalDirectoryName );

        NormalizedPath StackGitPath => DevDirectory.AppendPart( _stackFolderName );

        NormalizedPath CKliMapping => TempPath.AppendPart( _ckliMapping );

        /// <summary>
        /// Path to the folder storing the **Modification** to apply while testing.
        /// </summary>
        static NormalizedPath ModificationsFolder => TestHelper.TestProjectFolder.AppendPart( "Modifications" );


        /// <summary>
        /// Create a <see cref="TestUniverse"/> in a given folder.
        /// </summary>
        /// <param name="path"> The path where the <see cref="TestUniverse"/> will be. The Directory will be deleted when disposed.</param>
        /// <returns></returns>
        public static TestUniverse Create( IActivityMonitor m, NormalizedPath path, string imageName )
        {
            NormalizedPath ckliPath = path.AppendPart( _ckliMapping );
            if( Directory.Exists( ckliPath ) ) ReplaceInDirectoriesPaths( ckliPath, GitWorldStore.CleanPathDirName( ImageManager.PlaceHolderString ), GitWorldStore.CleanPathDirName( path ) );
            FakeApplicationLifetime appLife = new FakeApplicationLifetime();
            PlaceHolderSwapEverything( m, path, ImageManager.PlaceHolderString, path );
            var userHost = new UserHost( appLife, ckliPath );
            var output = new TestUniverse( m, path, userHost, imageName );
            userHost.Initialize( m );
            userHost.WorldStore.DeleteStackDefinition( m, "CK" );
            userHost.WorldStore.DeleteStackDefinition( m, "CK-Build" );
            return output;
        }

        /// <summary>
        /// Replace in all the Git Repositories the
        /// </summary>
        /// <param name="m"></param>
        /// <param name="oldString"></param>
        /// <param name="newString"></param>
        /// <returns></returns>
        public static int SwapAllGitOriginPlaceholders( IActivityMonitor m, NormalizedPath tempPath, string oldString, string newString )
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


        /// <summary>
        /// Apply the modifications stored as a zip in the Folder Modifications
        /// </summary>
        public void ApplyModifications( string modificationName )
        {
            ZipFile.ExtractToDirectory( ModificationsFolder.AppendPart( modificationName ), TempPath );
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
            int cnt = SwapAllGitOriginPlaceholders( m, tempPath, oldString, newString);
            ReplacePlaceHolderInFile( ckliMapping.AppendPart( "WorldLocalMapping.txt" ), oldString, newString );
            ReplacePlaceHolderInFile( ckliMapping.AppendPart( "Stacks.txt" ), oldString, newString );
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

        public void Dispose()
        {
            UserHost.Dispose();
            PlaceHolderSwapEverything( _m, TempPath, TempPath, ImageManager.PlaceHolderString );
            ReplaceInDirectoriesPaths( CKliMapping, GitWorldStore.CleanPathDirName( TempPath ), GitWorldStore.CleanPathDirName( ImageManager.PlaceHolderString ) );
            string output = ImageManager.GetImagePath( ImageName, false, true );
            File.Delete( output );
            ZipFile.CreateFromDirectory( TempPath, output );
            FileHelper.RawDeleteLocalDirectory( _m, TempPath );
        }
    }
}
