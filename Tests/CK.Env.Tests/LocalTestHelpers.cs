using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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
        readonly NormalizedPath _tempPath;
        public readonly UserHost UserHost;
        const string _fakeRemoteFeedName = "FakeRemoteFeed";
        const string _devDirectoryName = "dev";
        TestHost( NormalizedPath tempPath, FakeApplicationLifetime fakeApplicationLifetime, UserHost userHost )
        {
            _tempPath = tempPath;
            UserHost = userHost;
        }


        /// <summary>
        /// Create a test host in a temp directory with initialized tests stacks.
        /// </summary>
        /// <returns></returns>
        public static TestHost CreateTestHost()
        {
            FakeApplicationLifetime appLife = new FakeApplicationLifetime();
            NormalizedPath tempPath = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            Directory.CreateDirectory( tempPath );
            var userHost = new UserHost( appLife, tempPath );
            userHost.Initialize( TestHelper.Monitor );
            Directory.CreateDirectory( tempPath.AppendPart( _fakeRemoteFeedName ) );
            Directory.CreateDirectory( tempPath.AppendPart( _devDirectoryName ) );
            return new TestHost( tempPath, appLife, userHost );
        }

        public OpenedWorld OpenWorld( string worldName ) => OpenedWorld.OpenWorld( UserHost.WorldSelector, worldName );

        public class OpenedWorld : IDisposable
        {
            public World World => _worldSelector.CurrentWorld;
            readonly WorldSelector _worldSelector;

            OpenedWorld( WorldSelector worldSelector )
            {
                _worldSelector = worldSelector;
            }

            public static OpenedWorld OpenWorld( WorldSelector worldSelector, string worldName )
            {
                if( !worldSelector.Open( TestHelper.Monitor, worldName ) ) return null;
                return new OpenedWorld( worldSelector );
            }

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

        static NormalizedPath CKTestZipPath => TestHelper.TestProjectFolder.AppendPart( "CKTest.zip" );

        static ZipArchive CKTestZipArchive => new ZipArchive( File.OpenRead( CKTestZipPath ) );

        static IEnumerable<ZipArchiveEntry> CKTestZipWorldEntries => CKTestZipArchive.Entries.Where( p => new NormalizedPath( p.FullName ).FirstPart == "Worlds" );



        NormalizedPath WorldsFolder => _tempPath.AppendPart( "Worlds" );
        NormalizedPath StackGitPath => _tempPath.AppendPart( "CKTest-Stack" );
        NormalizedPath FakeRemoteFeedPath => _tempPath.AppendPart( _fakeRemoteFeedName );

        NormalizedPath DevDirectory => _tempPath.AppendPart( _devDirectoryName );

        void DezipCKTest()
        {
            NormalizedPath zipPath = CKTestZipPath;
            ZipFile.ExtractToDirectory( zipPath, _tempPath );
        }

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
                repo.Commit( "Replaced placeholds", signature, signature );
                repo.Network.Push( repo.Head );
            }
            DeleteDirectory( TestHelper.Monitor, tempGit );
            return worlds;
        }
        /// <summary>
        /// From https://github.com/libgit2/libgit2sharp/blob/f8e2d42ed9051fa5a5348c1a13d006f0cc069bc7/LibGit2Sharp.Tests/TestHelpers/DirectoryHelper.cs#L40
        /// </summary>
        /// <param name="directoryPath"></param>
        public static void DeleteDirectory(IActivityMonitor m, string directoryPath )
        {
            // From http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true/329502#329502

            if( !Directory.Exists( directoryPath ) )
            {
                m.Trace( string.Format( "Directory '{0}' is missing and can't be removed.", directoryPath ) );
                return;
            }
            NormalizeAttributes( directoryPath );
            DeleteDirectory(m, directoryPath, maxAttempts: 5, initialTimeout: 16, timeoutFactor: 2 );
        }
        static readonly Type[] _whitelist = { typeof( IOException ), typeof( UnauthorizedAccessException ) };
        private static void DeleteDirectory(IActivityMonitor m, string directoryPath, int maxAttempts, int initialTimeout, int timeoutFactor )
        {
            for( int attempt = 1; attempt <= maxAttempts; attempt++ )
            {
                try
                {
                    Directory.Delete( directoryPath, true );
                    return;
                }
                catch( Exception ex )
                {
                    var caughtExceptionType = ex.GetType();

                    if( !_whitelist.Any( knownExceptionType => knownExceptionType.GetTypeInfo().IsAssignableFrom( caughtExceptionType ) ) )
                    {
                        throw;
                    }

                    if( attempt < maxAttempts )
                    {
                        Thread.Sleep( initialTimeout * (int)Math.Pow( timeoutFactor, attempt - 1 ) );
                        continue;
                    }

                    m.Trace( string.Format( "{0}The directory '{1}' could not be deleted ({2} attempts were made) due to a {3}: {4}" +
                                                  "{0}Most of the time, this is due to an external process accessing the files in the temporary repositories created during the test runs, and keeping a handle on the directory, thus preventing the deletion of those files." +
                                                  "{0}Known and common causes include:" +
                                                  "{0}- Windows Search Indexer (go to the Indexing Options, in the Windows Control Panel, and exclude the bin folder of LibGit2Sharp.Tests)" +
                                                  "{0}- Antivirus (exclude the bin folder of LibGit2Sharp.Tests from the paths scanned by your real-time antivirus)" +
                                                  "{0}- TortoiseGit (change the 'Icon Overlays' settings, e.g., adding the bin folder of LibGit2Sharp.Tests to 'Exclude paths' and appending an '*' to exclude all subfolders as well)",
                        Environment.NewLine, Path.GetFullPath( directoryPath ), maxAttempts, caughtExceptionType, ex.Message ) );
                }
            }
        }

        private static void NormalizeAttributes( string directoryPath )
        {
            string[] filePaths = Directory.GetFiles( directoryPath );
            string[] subdirectoryPaths = Directory.GetDirectories( directoryPath );

            foreach( string filePath in filePaths )
            {
                File.SetAttributes( filePath, FileAttributes.Normal );
            }
            foreach( string subdirectoryPath in subdirectoryPaths )
            {
                NormalizeAttributes( subdirectoryPath );
            }
            File.SetAttributes( directoryPath, FileAttributes.Normal );
        }

        /// <summary>
        /// Ensure the stacks given, will remove the stacks CK and CK-Build.
        /// </summary>
        /// <param name="worlds"></param>
        void EnsureStacks( string[] worlds )
        {
            foreach( string world in worlds )
            {
                UserHost.WorldStore.EnsureStackDefinition( TestHelper.Monitor, world, new Uri( StackGitPath ).AbsoluteUri, true, DevDirectory );
            }
            UserHost.WorldStore.DeleteStackDefinition( TestHelper.Monitor, "CK" );
            UserHost.WorldStore.DeleteStackDefinition( TestHelper.Monitor, "CK-Build" );
        }

        public string[] AddTestStack()
        {
            DezipCKTest(); // We now have two directories: CKTest-Stack(The git containing the .Worlds) and Worlds repositories.
            string[] worlds = SetStacksDefaultConfig();
            EnsureStacks( worlds );
            return worlds;
        }



        /// <summary>
        /// Get the default world configuration with the placeholders.
        /// The xml is not valid !
        /// </summary>
        /// <param name="worldName"></param>
        /// <returns></returns>
        static string GetDefaultWorld( string worldName )
        {
            using( Stream fileStream = CKTestZipWorldEntries.Single( p => new NormalizedPath( p.FullName ).LastPart == worldName ).Open() )
            using( StreamReader reader = new StreamReader( fileStream ) )
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Get a default configured world config file.
        /// </summary>
        /// <param name="worldName"></param>
        /// <param name="worldsGitPath"></param>
        /// <param name="fakeRemoteFeedPath"></param>
        /// <returns></returns>
        public static string GetWorldWithDefaultConfiguration( string worldName, NormalizedPath worldsGitPath, NormalizedPath fakeRemoteFeedPath )
        {
            return ReplaceWorldPlaceHolder( GetDefaultWorld( worldName ), TestWorldConfig.DefaultConfig( worldsGitPath, fakeRemoteFeedPath ) );
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

        public static void ReplacePlaceHolderInConfig( NormalizedPath configPath, TestWorldConfig worldConfig )
        {
            File.WriteAllText( configPath, ReplaceWorldPlaceHolder( File.ReadAllText( configPath ), worldConfig ) );
        }

        public void Dispose()
        {
            UserHost.Dispose();
            DeleteDirectory( TestHelper.Monitor, _tempPath );
        }
    }
}
