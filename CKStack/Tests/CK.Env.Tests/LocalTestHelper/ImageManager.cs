using CK.Core;

using LibGit2Sharp;
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
    /// Helpers methods to manage images.
    /// </summary>
    public static class ImageManager
    {
        /// <summary>
        /// Path to the folder storing the **Universe Zips** used for the Tests.
        /// </summary>
        public static NormalizedPath CacheUniverseFolder => TestHelper.TestProjectFolder.AppendPart( "UniverseZips" );

        public static NormalizedPath WorkFolder = Path.Combine( Path.GetTempPath(), "CKliTest" );

        /// <summary>
        /// Path to the folder storing the Seed Zips.
        /// </summary>
        public static NormalizedPath SeedUniverseFolder => TestHelper.TestProjectFolder.AppendPart( "SeedZips" );

        /// <summary>
        /// Instantiate a <see cref="TestUniverse"/> from the given path.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="imagePath"></param>
        /// <returns></returns>
        public static TestUniverse InstantiateImage( IActivityMonitor m, NormalizedPath imagePath )
        {
            if( !File.Exists( imagePath ) ) throw new FileNotFoundException( nameof( imagePath ) );

            m.Info( $"Deleting '{WorkFolder}' content.'" );
            if( Directory.Exists( WorkFolder ) )
            {
                FileHelper.RawDeleteLocalDirectory( TestHelper.Monitor, WorkFolder );
            }
            m.Info( $"Creating temp directory '{WorkFolder}' and unzipping '{imagePath}' into." );
            Directory.CreateDirectory( WorkFolder );
            ZipFile.ExtractToDirectory( imagePath, WorkFolder );
            if( Directory.Exists( Path.Combine( WorkFolder, Path.GetFileNameWithoutExtension( imagePath ) ) ) )
            {
                throw new InvalidDataException( "Archive content is embedded in a directory. The content should be directly at the root." );
            }
            return TestUniverse.Create( m, WorkFolder );
        }

        public static NormalizedPath EnsureImage<T>(
            Func<T?, bool, NormalizedPath> imageGenerator,
            bool refreshCache )
        {
            NormalizedPath generatedBaseImagePath = CacheUniverseFolder.AppendPart( imageGenerator.Method.Name + ".zip" );
            if( !refreshCache && File.Exists( generatedBaseImagePath ) ) return generatedBaseImagePath;
            if( File.Exists( generatedBaseImagePath ) ) File.Delete( generatedBaseImagePath );
            return imageGenerator( default, refreshCache );
        }

        public static TestUniverse InstantiateImage<T>(
            IActivityMonitor m,
            Func<T?, bool, NormalizedPath> parentImageGenerator,
            bool refreshCache )
        {
            if( parentImageGenerator == null ) throw new ArgumentNullException();
            NormalizedPath generatedBaseImagePath = EnsureImage( parentImageGenerator, refreshCache );
            return InstantiateImage( m, generatedBaseImagePath );
        }

        public class ZipComparer : IDisposable
        {
            readonly ZipArchive _a;
            readonly ZipArchive _b;
            static readonly ZipEntryComparer _comparer = new ZipEntryComparer();
            class ZipEntryComparer : IEqualityComparer<ZipArchiveEntry>
            {
                public bool Equals( ZipArchiveEntry? x, ZipArchiveEntry? y )
                {
                    return x?.Crc32 == y?.Crc32 && x?.FullName == y?.FullName;
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
