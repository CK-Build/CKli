using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using CK.Core;
using CK.Text;
using CKli;
using LibGit2Sharp;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests
{
    public class TestHost : IDisposable
    {
        /// <summary>
        /// Temp path of the TestHost.
        /// </summary>
        readonly NormalizedPath _tempPath;

        /// <summary>
        /// The <see cref="UserHost"/> used to run tests on.
        /// </summary>
        public readonly UserHost UserHost;

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

        /// <summary>
        /// Instantiate a new <see cref="TestHost"/>.
        /// </summary>
        /// <param name="tempPath">Path of the TestHost.</param>
        /// <param name="userHost">The UserHost instantied with this path.</param>
        TestHost( NormalizedPath tempPath, UserHost userHost )
        {
            _tempPath = tempPath;
            UserHost = userHost;
        }


        /// <summary>
        /// Create a test host in a temp directory.
        /// This TestHost has no stacks.
        /// </summary>
        /// <returns></returns>
        public static TestHost Create()
        {
            FakeApplicationLifetime appLife = new FakeApplicationLifetime();
            NormalizedPath tempPath = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            Directory.CreateDirectory( tempPath );
            var userHost = new UserHost( appLife, tempPath );
            userHost.Initialize( TestHelper.Monitor );
            userHost.WorldStore.DeleteStackDefinition( TestHelper.Monitor, "CK" );
            userHost.WorldStore.DeleteStackDefinition( TestHelper.Monitor, "CK-Build" );
            Directory.CreateDirectory( tempPath.AppendPart( _fakeRemoteFeedName ) );
            Directory.CreateDirectory( tempPath.AppendPart( _devDirectoryName ) );
            return new TestHost( tempPath, userHost );
        }

        /// <summary>
        /// Simple class encapsulating an opened World.
        /// </summary>
        public class OpenedWorld : IDisposable
        {
            public World World => _worldSelector.CurrentWorld;
            readonly WorldSelector _worldSelector;

            OpenedWorld( WorldSelector worldSelector )
            {
                _worldSelector = worldSelector;
            }
            /// <summary>
            /// Open a World and encapsulate it in an OpenedWorld.
            /// </summary>
            /// <param name="worldSelector"></param>
            /// <param name="worldName"></param>
            /// <returns></returns>
            public static OpenedWorld OpenWorld( WorldSelector worldSelector, string worldName )
            {
                if( !worldSelector.Open( TestHelper.Monitor, worldName ) ) return null;
                return new OpenedWorld( worldSelector );
            }
            /// <summary>
            /// Close the world.
            /// </summary>
            public void Dispose()
            {
                _worldSelector.Close( TestHelper.Monitor );
            }
        }

        public class TestWorldConfig
        {
            public static TestWorldConfig DefaultConfig( NormalizedPath worldsGitPath, NormalizedPath fakeRemoteFeedPath )
            {
                return new TestWorldConfig()
                {
                    TargetRepoNpm = fakeRemoteFeedPath,
                    SourceFeedNpm = fakeRemoteFeedPath,
                    SourceFeedNuget = fakeRemoteFeedPath,
                    TargetRepoNuget = fakeRemoteFeedPath,
                    WorldPath = worldsGitPath
                };
            }
            public string TargetRepoNuget { get; set; }
            public string TargetRepoNpm { get; set; }
            public string WorldPath { get; set; }
            public string SourceFeedNuget { get; set; }
            public string SourceFeedNpm { get; set; }
        }

        /// <summary>
        /// Path to the folder storing the **Universe Zips** used for the Tests.
        /// </summary>
        static NormalizedPath TestsUniverseFolder => TestHelper.TestProjectFolder.AppendPart( "UniverseZips" );

        /// <summary>
        /// Path to the folder storing the builded **Images**.
        /// </summary>
        static NormalizedPath BuildedImageFolder => TestHelper.TestProjectFolder.AppendPart( "Images" );

        /// <summary>
        /// Path to the folder storing the **Modification** to apply while testing.
        /// </summary>
        static NormalizedPath ModificationsFolder => TestHelper.TestProjectFolder.AppendPart( "Modifications" );

        /// <summary>
        /// Path to the Folder where all the Worlds are stored.
        /// </summary>
        NormalizedPath WorldsFolder => _tempPath.AppendPart( _worldsFolderName );

        /// <summary>
        /// Path to the <see cref="Repository"/> storing the Stacks.
        /// </summary>
        NormalizedPath StackGitPath => _tempPath.AppendPart( _stackFolderName );

        /// <summary>
        /// Path to the Folder used as a Remote Feed.
        /// </summary>
        NormalizedPath FakeRemoteFeedPath => _tempPath.AppendPart( _fakeRemoteFeedName );

        /// <summary>
        /// Path to the Folder used to clone the Worlds Repositories.
        /// </summary>
        NormalizedPath DevDirectory => _tempPath.AppendPart( _devDirectoryName );

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
        /// <param name="inGeneratedFolder">Whether the image is stored in <see cref="BuildedImageFolder"/>(<see langword="true"/>) or </param>
        /// <returns></returns>
        public bool IsEqualToImage( string imageName, bool inGeneratedFolder )
        {
            using( var truth = CreateWithUniverse( inGeneratedFolder, null, imageName ) )
            {
                return IsEqualToTestHost( truth );
            }
        }

        public bool IsEqualToTestHost(TestHost testHost)
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

        /// <summary>
        /// Create a test host and add the worlds of a universe, based on the callerName.
        /// </summary>
        /// <param name="recreateUniverseFromScratch">Should the image used to start this universe be recreated from scratch ?</param>
        /// <param name="imageParentCreator">This should be the method that recreate the image and store it in <see cref="BuildedImageFolder"/>.</param>
        /// <param name="arbitraryName">Arbitrary optional name, take <see cref="callerMemberName"/> if null </param>
        /// <param name="callerMemberName"></param>
        /// <returns></returns>
        public static TestHost CreateWithUniverse( bool recreateUniverseFromScratch, Action<bool> imageParentCreator, string arbitraryName = null, [CallerMemberName] string callerMemberName = null )
        {
            if( recreateUniverseFromScratch && imageParentCreator != null ) imageParentCreator( true );
            var host = Create();
            host.LoadUniverse( arbitraryName ?? callerMemberName, recreateUniverseFromScratch );
            return host;
        }

        /// <summary>
        /// Load an image in the current TestHost. Should be run only once.
        /// </summary>
        /// <param name="universeName"></param>
        /// <param name="generatedImage"></param>
        void LoadImage( string universeName, bool generatedImage )
        {
            var folder = generatedImage ? BuildedImageFolder : TestsUniverseFolder;
            ZipFile.ExtractToDirectory( folder.AppendPart( universeName + ".zip" ), _tempPath );
        }

        /// <summary>
        /// Apply the modifications stored as a zip in the Folder Modifications
        /// </summary>
        void ApplyModifications( string modificationName )
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Replace the placeholders in the configs with the default config.
        /// </summary>
        /// <returns></returns>
        string[] SetStacksDefaultConfig()
        {
            string[] worlds;
            NormalizedPath tempGit = _tempPath.AppendPart( Path.GetRandomFileName() );
            using( var repo = new Repository( Repository.Clone( new Uri( StackGitPath ).AbsoluteUri, tempGit ) ) )
            {
                var files = Directory.GetFiles( tempGit ).Where( p => p != ".git" ).ToArray();
                worlds = files.Select( p => Path.GetFileNameWithoutExtension( p ).Replace( ".World", "" ) ).ToArray();
                foreach( string fileName in files )
                {
                    ReplacePlaceHolderInConfig( fileName, TestWorldConfig.DefaultConfig( WorldsFolder, FakeRemoteFeedPath ) );
                }
                Commands.Stage( repo, files );
                Signature signature = new Signature( new Identity( "CKli_UnitTest", "test@signature-code.com" ), DateTimeOffset.UtcNow );

                if( repo.RetrieveStatus().IsDirty ) repo.Commit( "Replaced placeholds", signature, signature );
                repo.Network.Push( repo.Head );
            }
            FileHelper.DeleteDirectory( TestHelper.Monitor, tempGit );
            return worlds;
        }


        /// <summary>
        /// Ensure the given stacks in the WorldStore.
        /// </summary>
        /// <param name="worlds">Worlds to ensure.</param>
        void EnsureStacks( string[] worlds )
        {
            foreach( string world in worlds )
            {
                UserHost.WorldStore.EnsureStackDefinition( TestHelper.Monitor, world, new Uri( StackGitPath ).AbsoluteUri, true, DevDirectory );
            }
        }

        /// <summary>
        /// Unzip in the <see cref="_tempPath"/> a zip representing an universe.
        /// </summary>
        /// <param name="universeName">Name of the universe to add.</param>
        /// <param name="generatedImage"></param>
        /// <returns></returns>
        public string[] LoadUniverse( string universeName, bool generatedImage )
        {
            LoadImage( universeName, generatedImage ); // We now have two directories: CKTest-Stack(The git containing the .Worlds) and Worlds repositories.
            string[] stacks = SetStacksDefaultConfig();
            EnsureStacks( stacks );
            return stacks;
        }

        /// <summary>
        /// Replace all the placeHolder. The output xml should is valid.
        /// </summary>
        /// <param name="worldTxt"></param>
        /// <param name="worldConfig"></param>
        /// <returns></returns>
        public static string ReplaceWorldPlaceHolder( string worldTxt, TestWorldConfig worldConfig )
        {
            string A( string a ) => "\"" + a;
            const string placeholderTargetRepoNuget = "<\"PLACEHOLDER_TARGET_REPOSITORY_NUGET_URI";
            const string placeholderTargetRepoNpm = "<\"PLACEHOLDER_TARGET_REPOSITORY_NPM_URI";
            const string placeholderWorldPath = "<\"PLACEHOLDER_WORLDS_PATH";
            const string placeholderSourceFeedNuget = "<\"PLACEHOLDER_SOURCE_FEED_NUGET_URI";
            const string placeholderSourceFeedNpm = "<\"PLACEHOLDER_SOURCE_FEED_NPM_URI";
            string output = worldTxt.Replace( placeholderTargetRepoNuget, A( worldConfig.TargetRepoNuget ) )
                .Replace( placeholderTargetRepoNpm, A( worldConfig.TargetRepoNpm ) )
                .Replace( placeholderWorldPath, A( worldConfig.WorldPath ) )
                .Replace( placeholderSourceFeedNuget, A( worldConfig.SourceFeedNuget ) )
                .Replace( placeholderSourceFeedNpm, A( worldConfig.SourceFeedNpm ) );
            Debug.Assert( !output.Contains( "<\"" ) );
            return output;
        }

        /// <summary>
        /// Replace the placeholders in the World file.
        /// </summary>
        /// <param name="configPath">Path of the World file.</param>
        /// <param name="worldConfig">The config to apply on the World.</param>
        public static void ReplacePlaceHolderInConfig( NormalizedPath configPath, TestWorldConfig worldConfig )
        {
            File.WriteAllText( configPath, ReplaceWorldPlaceHolder( File.ReadAllText( configPath ), worldConfig ) );
        }


        /// <summary>
        /// Build an image from the current TestHost.
        /// Allow to restore a TestHost with the FileSystem same state.
        /// </summary>
        /// <param name="arbitraryName">The name of the image to generate. Default to <paramref name="callerMemberName"/> if null.</param>
        /// <param name="callerMemberName"></param>
        public void BuildImage( string arbitraryName = null, [CallerMemberName] string callerMemberName = null )
        {
            string universeName = arbitraryName ?? callerMemberName;
            string zipPath = BuildedImageFolder.AppendPart( universeName + ".zip" );
            File.Delete( zipPath );
            ZipFile.CreateFromDirectory( _tempPath, zipPath );
            using( ZipArchive archive = ZipFile.Open( zipPath, ZipArchiveMode.Update ) )
            {
                var entries = archive.Entries.Where( p => !p.FullName.StartsWith( _stackFolderName ) && !p.FullName.StartsWith( _worldsFolderName ) ).ToList();
                foreach( var toDelete in entries )
                {
                    toDelete.Delete();
                }
            }
        }

        /// <summary>
        /// Dispose the <see cref="UserHost"/> and delete the temporary test.
        /// </summary>
        public void Dispose()
        {
            UserHost.Dispose();
            FileHelper.DeleteDirectory( TestHelper.Monitor, _tempPath );
        }
    }
}
