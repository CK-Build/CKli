using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests.LocalTestHelper
{
    /// <summary>
    /// Manage images. Diposing this class will delete all the generated images.
    /// </summary>
    public static class ImageManager
    {
        /// <summary>
        /// Path to the folder storing the **Universe Zips** used for the Tests.
        /// </summary>
        public static NormalizedPath CacheUniverseFolder => TestHelper.TestProjectFolder.AppendPart( "UniverseZips" );

        /// <summary>
        /// Path to the folder storing the Seed Zips.
        /// </summary>
        public static NormalizedPath SeedUniverseFolder => TestHelper.TestProjectFolder.AppendPart( "SeedZips" );

        public static NormalizedPath WorkFolder => Path.Combine( Path.GetTempPath(), "CKliTestWorkFolder" );

        /// <summary>
        /// Create a zip file
        /// </summary>
        /// <param name="monitor"></param>
        /// <returns>Zip file full path</returns>
        public static NormalizedPath InitImageFromScratch( IActivityMonitor monitor )
        {
            string imageName = $"minimal_project_scratch.zip";
            string imageFullPath = Path.Combine( SeedUniverseFolder, imageName );

            var directories = new List<string>()
            {
                "CKTest-Stack",
                "dev",
                "FakeRemoteFeed",
                "Worlds"
            };

            NormalizedPath tempPath = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );

            DirectoryInfo directoryInfo = new DirectoryInfo( tempPath );
            foreach( var directory in directories )
                directoryInfo.CreateSubdirectory( directory );

            if( File.Exists( imageFullPath ) ) File.Delete( imageFullPath );
            ZipFile.CreateFromDirectory( tempPath, imageFullPath );

            return imageFullPath;
        }

        /// <summary>
        /// Instantiate a <see cref="TestUniverse"/> from the given path.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="imagePath"></param>
        /// <returns></returns>
        public static TestUniverse InstantiateImage( IActivityMonitor m, NormalizedPath imagePath )
        {
            if( !File.Exists( imagePath ) ) throw new FileNotFoundException( nameof( imagePath ) );
            m.Info( $"Creating temp directory {WorkFolder} and dezipping '{imagePath}' into." );
            if( Directory.Exists( WorkFolder ) )
            {
                m.Info( $"Deleting '{WorkFolder} content.'" );
                FileHelper.RawDeleteLocalDirectory( TestHelper.Monitor, WorkFolder );
            }
            Directory.CreateDirectory( WorkFolder );
            ZipFile.ExtractToDirectory( imagePath, WorkFolder );
            return TestUniverse.Create( m, WorkFolder );
        }

        public static NormalizedPath EnsureImage<T>(
            Func<T, bool, NormalizedPath> imageGenerator,
            bool refreshCache )
        {
            NormalizedPath generatedBaseImagePath = CacheUniverseFolder.AppendPart( imageGenerator.Method.Name + ".zip" );
            if( !refreshCache && File.Exists( generatedBaseImagePath ) ) return generatedBaseImagePath;
            if( File.Exists( generatedBaseImagePath ) ) File.Delete( generatedBaseImagePath );
            return imageGenerator( default, refreshCache );
        }

        public static TestUniverse InstantiateImage<T>(
            IActivityMonitor m,
            Func<T, bool, NormalizedPath> parentImageGenerator,
            bool refreshCache )
        {
            if( parentImageGenerator == null ) throw new ArgumentNullException();
            NormalizedPath generatedBaseImagePath = EnsureImage( parentImageGenerator, refreshCache );
            return InstantiateImage( m, generatedBaseImagePath );
        }

        //public static TestUniverse AddSolutionToImage
        //(
        //    IActivityMonitor m,
        //   NormalizedPath imagePath,
        //    string solutionName
        //)
        //{
        //    TestUniverse universe = InstantiateImage( m, imagePath );

        //    ZipFile.ExtractToDirectory( Path.Combine( BareReposFolder, solutionName, ".zip" ), universe.StackBareGitPath );

        //    return universe;
        //}

        public class ZipComparer : IDisposable
        {
            readonly ZipArchive _a;
            readonly ZipArchive _b;
            static readonly ZipEntryComparer _comparer = new ZipEntryComparer();
            class ZipEntryComparer : IEqualityComparer<ZipArchiveEntry>
            {
                public bool Equals( ZipArchiveEntry x, ZipArchiveEntry y )
                {
                    return x.Crc32 == y.Crc32 && x.FullName == y.FullName;
                }

                public int GetHashCode( ZipArchiveEntry obj )
                {
                    return (int)obj.Crc32;
                }
            }
            internal ZipComparer( ZipArchive a, ZipArchive b )
            {
                _a = a;
                _b = b;
            }

            public void Dispose()
            {
                _a.Dispose();
                _b.Dispose();
            }

            public IEnumerable<ZipArchiveEntry> AExceptB => _a.Entries.Except( _b.Entries, _comparer );

            public IEnumerable<ZipArchiveEntry> BExceptA => _b.Entries.Except( _a.Entries, _comparer );

        }

        public static ZipComparer CompareBuildedImages( string imageAName, string imageBName ) =>
            CompareBuiltImages(
                CacheUniverseFolder.AppendPart( imageAName + ".zip" ),
                CacheUniverseFolder.AppendPart( imageBName + ".zip" )
            );

        public static ZipComparer CompareBuiltImages( NormalizedPath imageA, NormalizedPath imageB ) =>
            new ZipComparer(
                ZipFile.OpenRead( imageA ),
                ZipFile.OpenRead( imageB )
            );
    }
}
