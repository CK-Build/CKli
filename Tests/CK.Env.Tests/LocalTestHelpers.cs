using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        readonly FakeApplicationLifetime _fakeApplicationLifetime;
        public readonly UserHost UserHost;
        const string _fakeRemoteFeedName = "FakeRemoteFeed";
        const string _devDirectoryName = "dev";
        TestHost( NormalizedPath tempPath, FakeApplicationLifetime fakeApplicationLifetime, UserHost userHost )
        {
            _tempPath = tempPath;
            _fakeApplicationLifetime = fakeApplicationLifetime;
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
            return worlds;
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

        public void AddTestStack()
        {
            DezipCKTest(); // We now have two directories: CKTest-Stack(The git containing the .Worlds) and Worlds repositories.
            string[] worlds = SetStacksDefaultConfig();
            EnsureStacks( worlds );
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
            Directory.Delete( _tempPath, true );
        }
    }
}
