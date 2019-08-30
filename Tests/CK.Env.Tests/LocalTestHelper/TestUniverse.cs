using CK.Core;
using CK.Text;
using CKli;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

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

        const string _userLocalDirectoryName = "Local";

        private readonly ImageManager _imageManager;

        /// <summary>
        /// Instantiate a new <see cref="TestUniverse"/>.
        /// </summary>
        /// <param name="tempPath">Path of the TestHost.</param>
        /// <param name="userHost">The UserHost instantied with this path.</param>
        /// <param name="imageName">Name of the image this <see cref="TestUniverse"/> is based on.</param>
        /// <param name="configs">The stacks configs.</param>
        TestUniverse( ImageManager imageManager, NormalizedPath tempPath, UserHost userHost, string imageName )
        {
            _imageManager = imageManager;
            TempPath = tempPath;
            UserHost = userHost;
            ImageName = imageName;
            Configs = new Dictionary<string, StackConfig>();
            ReloadConfig();
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
        public static TestUniverse Create( ImageManager imageManager, NormalizedPath path, string imageName )
        {
            FakeApplicationLifetime appLife = new FakeApplicationLifetime();
            var userHost = new UserHost( appLife, path.AppendPart( _ckliMapping ) );
            userHost.Initialize( TestHelper.Monitor );
            userHost.WorldStore.DeleteStackDefinition( TestHelper.Monitor, "CK" );
            userHost.WorldStore.DeleteStackDefinition( TestHelper.Monitor, "CK-Build" );



            return new TestUniverse( imageManager, path, userHost, imageName );
        }


        public void ReloadConfig()
        {
            var path = TempPath.AppendPart( _ckliMapping );
            var files = Directory.EnumerateDirectories( path )
                .Where( p => p != "Logs" )
                .SelectMany( p => Directory.EnumerateFiles( p ) )
                .Where( p => !p.Contains( ".git" ) && !p.Contains( "SharedState" ) );
            var config = new Dictionary<string, StackConfig>();
            foreach( string fileName in files )
            {
                var newConfig = StackConfig.Create( path, fileName );
                newConfig.PlaceHolderSwap( true );
                config.Add( fileName, newConfig);
            }
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
        public bool IsEqualToImage( string imageName, bool fromTempImage )
        {
            using( var truth = _imageManager.InstantiateImage( fromTempImage, imageName ) )
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

        public void BuildImage()
        {
            _imageManager.BuildImage( this );
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
            FileHelper.RawDeleteLocalDirectory( TestHelper.Monitor, TempPath );
        }
    }
}
