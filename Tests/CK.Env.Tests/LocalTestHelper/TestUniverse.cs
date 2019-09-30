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
        private readonly ImageManager _imageManager;

        /// <summary>
        /// Instantiate a new <see cref="TestUniverse"/>.
        /// </summary>
        /// <param name="tempPath">Path of the TestHost.</param>
        /// <param name="userHost">The UserHost instantied with this path.</param>
        /// <param name="imageName">Name of the image this <see cref="TestUniverse"/> is based on.</param>
        /// <param name="configs">The stacks configs.</param>
        TestUniverse( IActivityMonitor m, ImageManager imageManager, NormalizedPath tempPath, UserHost userHost, string imageName )
        {
            _m = m;
            _imageManager = imageManager;
            TempPath = tempPath;
            UserHost = userHost;
            ImageName = imageName;
            Configs = new Dictionary<string, StackConfig>();
            ReloadConfigAndGitsWithNewPaths( m );
        }

        public void ReloadConfigAndGitsWithNewPaths( IActivityMonitor m )
        {
            int cnt = SwapAllGitOriginPlaceholders( m, ImageManager.PlaceHolderString, TempPath );
            var files = Directory.EnumerateFiles( TempPath.AppendPart( _ckliMapping ), "*.World.xml", SearchOption.AllDirectories )
                .Where( p => !p.Contains( ".git" ) );
            var config = new Dictionary<string, StackConfig>();
            foreach( string fileName in files )
            {
                var newConfig = StackConfig.Create( TempPath, fileName );
                newConfig.PlaceHolderSwap( true, ImageManager.PlaceHolderString );
                newConfig.Save();
                config.Add( fileName, newConfig );
            }
        }

        /// <summary>
        /// The <see cref="UserHost"/> used to run tests on.
        /// </summary>
        public UserHost UserHost { get; }

        public Dictionary<string, StackConfig> Configs { get; }

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
        public static TestUniverse Create( IActivityMonitor m, ImageManager imageManager, NormalizedPath path, string imageName )
        {
            FakeApplicationLifetime appLife = new FakeApplicationLifetime();
            var userHost = new UserHost( appLife, path.AppendPart( _ckliMapping ) );
            userHost.Initialize( m );
            userHost.WorldStore.DeleteStackDefinition( m, "CK" );
            userHost.WorldStore.DeleteStackDefinition( m, "CK-Build" );

            return new TestUniverse( m, imageManager, path, userHost, imageName );
        }

        public int SwapAllGitOriginPlaceholders( IActivityMonitor m, string oldString, string newString )
        {
            using( m.OpenInfo( "Replacing Git Remotes URL." ) ) return Directory.EnumerateDirectories( TempPath, "*.git" ).Select( s => new Repository( s ) ).Select( s => (s, s.Network.Remotes, s.Network.Remotes.Single()) ).Count( ( s ) => { string oldUri = s.Item3.PushUrl; s.Remotes.Update( s.Item3.Name, a => { a.Url = s.Item3.Url.Replace( oldString, newString ); a.PushUrl = s.Item3.PushUrl.Replace( oldString, newString ); } ); m.Info( $"Replaced origin of repo {s.s.Info.Path} from {oldUri} to {s.Item3.Url}" ); s.s.Dispose(); return true; } );
        }



        /// <summary>
        /// Apply the modifications stored as a zip in the Folder Modifications
        /// </summary>
        public void ApplyModifications( string modificationName )
        {
            ZipFile.ExtractToDirectory( ModificationsFolder.AppendPart( modificationName ), TempPath );
        }

        /// <summary>
        /// Return all the Repositories' worlds.
        /// </summary>
        IEnumerable<NormalizedPath> AllGitRepos =>
            Directory.GetDirectories( WorldsFolder )
            .SelectMany( p => Directory.GetDirectories( p ) )
            .Select( p => new NormalizedPath( p ) );


        /// <summary>
        /// Test the equality with an Image.
        /// </summary>
        /// <param name="imageName">Image to use to do the comparison.</param>
        /// <param name="fromTempImage">Whether the image is stored in <see cref="BuildedImageFolder"/>(<see langword="true"/>)
        /// or in <see cref="TestsUniverseFolder"/></param>
        /// <returns></returns>
        public bool IsEqualToImage( IActivityMonitor m, string imageName, bool fromTempImage )
        {
            using( var truth = _imageManager.InstantiateImage( m, fromTempImage, imageName ) )
            {
                return IsEqualToTestUniverse( truth );
            }
        }

        public bool IsEqualToTestUniverse( TestUniverse testHost )
        {
            var repos = AllGitRepos.Select( p => new Repository( p ) );
            var truthRepo = testHost.AllGitRepos.Select( p => new Repository( p ) );

            var truthpath = GetAllFilePathOfRepos( truthRepo ).ToList();
            var paths = GetAllFilePathOfRepos( repos ).ToList();
            foreach( NormalizedPath path in paths )
            {
                if( !truthpath.Remove( path ) )
                {
                    if( truthpath.Contains( path ) ) throw new InvalidOperationException();//Should be true ... Maybe concurrent acces ?
                    return false;
                }
            }
            return truthpath.Count() == 0;
        }

        public void BuildImage( IActivityMonitor m )
        {
            _imageManager.BuildImage( m, this );
        }

        IEnumerable<NormalizedPath> GetAllFilePathOfRepos( IEnumerable<Repository> repos )
        {
            return repos.SelectMany( p =>
            {
                var a = p.Branches.Select( q => q.Tip ).ToList();
                if( a.Count == 0 ) return new NormalizedPath[0];
                return AllFileFullPath( "/", a.MaxBy( r => r.Author.When ).Tree );
            } );
        }

        IEnumerable<NormalizedPath> AllFileFullPath( NormalizedPath treePath, Tree tree )
        {
            return tree.Where( x => x.TargetType == TreeEntryTargetType.Tree )
                 .SelectMany( x => AllFileFullPath( treePath.AppendPart( x.Name ), (Tree)x.Target ) )
                 .Concat( tree.Where( x => x.TargetType == TreeEntryTargetType.Blob ).Select( p => treePath.AppendPart( p.Name + "-" + p.Target.Sha ) ) );
        }

        public void Dispose()
        {
            UserHost.Dispose();
            FileHelper.RawDeleteLocalDirectory( _m, TempPath );
        }
    }
}
