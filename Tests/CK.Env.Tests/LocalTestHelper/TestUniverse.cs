using CK.Core;
using CK.Env.DependencyModel;
using CK.SimpleKeyVault;

using CKli;
using CSemVer;
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

        static XTypedFactory? _xTypedFactory;

        /// <summary>
        /// Instantiates a new <see cref="TestUniverse"/>.
        /// </summary>
        /// <param name="tempPath">Path of the TestHost.</param>
        /// <param name="userHost">The UserHost instantiated with this path.</param>
        TestUniverse( NormalizedPath tempPath ) => UniversePath = tempPath;

        void Open( IActivityMonitor m )
        {
            NormalizedPath ckliPath = UniversePath.AppendPart( _ckliMapping );
            var mappings = new FileWorldLocalMapping( ckliPath.AppendPart( "WorldLocalMapping.txt" ) );
            var keyVault = new FileKeyVault( ckliPath.AppendPart( "Personal.KeyVault.txt" ) );
            keyVault.OpenKeyVault( m );
            UserHost = MultipleWorldHome.Create( m, ckliPath, _xTypedFactory!, keyVault, mappings, () => new FakeReleaseVersionSelector() );
            UserHost.WorldStore.DestroyWorld( m, "CK" );
            UserHost.WorldStore.DestroyWorld( m, "CK-Build" );
        }

        public void Restart(IActivityMonitor m)
        {
            Dispose();
            Open( m );
        }

        /// <summary>
        /// The <see cref="UserHost"/> used to run tests on.
        /// </summary>
        public MultipleWorldHome UserHost { get; private set; } = null!; //Set by Open() which is called by the factory method.

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

        class FakeReleaseVersionSelector : IReleaseVersionSelector
        {
            public void ChooseFinalVersion( IActivityMonitor m, IReleaseVersionSelectorContext context )
            {
                throw new NotImplementedException();
            }

            public bool OnAlreadyReleased( IActivityMonitor m, DependentSolution solution, CSVersion version, bool isContentTag )
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Create a <see cref="TestUniverse"/> in a given folder.
        /// </summary>
        /// <param name="path"> The path where the <see cref="TestUniverse"/> will be. The Directory will be deleted when disposed.</param>
        /// <returns></returns>
        public static TestUniverse Create( IActivityMonitor m, NormalizedPath path )
        {
            m.Info( $"Creating TestUniverse from {path}." );
            if( _xTypedFactory == null )
            {
                System.Reflection.Assembly.Load( "CKli.XObject" );
                System.Reflection.Assembly.Load( "CKli" );
                _xTypedFactory = new XTypedFactory();
                _xTypedFactory.AutoRegisterFromLoadedAssemblies( m );
                _xTypedFactory.SetLocked();
            }
            TestUniverse output = new( path );
            output.Open( m );
            return output;
        }

        public static void ChangeStringInAllSubPathAndFileContent( IActivityMonitor m, NormalizedPath folder, string oldString, string newString )
        {
            // It change all the mention of a string to another one, it's the implementation of the Ministry of Truth.
            string[] dirs = Directory.GetDirectories( folder );
            for( int i = 0; i < dirs.Length; i++ )
            {
                ChangeStringInAllSubPathAndFileContent( m, dirs[i], oldString, newString );
                RenameFolder( m, dirs[i], oldString, newString );
            }
            string[] files = Directory.GetFiles( folder );
            for( int i = 0; i < files.Length; i++ )
            {
                RenameFile( m, files[i], oldString, newString );
                ReplacePlaceHolderInFile( m, files[i], oldString, newString );
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

        }

        /// <summary>
        /// Create a ZipFile containing the current state of the app.
        /// </summary>
        /// <param name="imageName"></param>
        /// <returns></returns>
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

        public void Dispose() => UserHost.Dispose( TestHelper.Monitor );
    }
}
