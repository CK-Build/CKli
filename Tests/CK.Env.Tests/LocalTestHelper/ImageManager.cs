using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using static CK.Testing.MonitorTestHelper;

namespace CK.Env.Tests.LocalTestHelper
{
    /// <summary>
    /// Manage images. Diposing this class will delete all the generated images.
    /// </summary>
    public class ImageManager
    {
        /// <summary>
        /// Path to the folder storing the **Universe Zips** used for the Tests.
        /// </summary>
        static NormalizedPath TestsUniverseFolder => TestHelper.TestProjectFolder.AppendPart( "UniverseZips" );

        /// <summary>
        /// Path to the folder storing the Seed Zips.
        /// </summary>
        static NormalizedPath SeedUniverseFolder => TestHelper.TestProjectFolder.AppendPart( "SeedZips" );

        /// <summary>
        /// 
        /// </summary>
        /// <param name="imageName"></param>
        /// <param name="isSeedImage"></param>
        /// <param name="isBuildResult"><see langword="true"/> for builded, <see langword="false"/> for a base image.</param>
        /// <returns></returns>
        public static NormalizedPath GetImagePath( string imageName, bool isSeedImage, bool isBuildResult )
        {
            return (isSeedImage ? SeedUniverseFolder : TestsUniverseFolder).AppendPart( imageName + (isBuildResult ? "#builded" : "") + ".zip" );
        }

        /// <summary>
        /// Instantiate an image into a <see cref="TestUniverse"/> with a working folder.
        /// </summary>
        /// <param name="fromBuildedImage">False if the image is stored in <see cref="TestsUniverseFolder"/> or the <see cref="ImageManager"/> temp folder.</param>
        /// <param name="arbitraryCallName">If not null, will determine the name of the image.</param>
        /// <param name="callerMemberName">The caller member name. If <paramref name="arbitraryCallName"/> is null, it will used as the imageName</param>
        /// <returns></returns>
        public static TestUniverse InstantiateImage( IActivityMonitor m, bool useSeedImage, string arbitraryCallName = null, [CallerMemberName] string callerMemberName = null )
        {
            string imageName = arbitraryCallName ?? callerMemberName;
            return InstantiateImage( m, GetImagePath( imageName, useSeedImage, false ) );
        }

        static TestUniverse InstantiateImage( IActivityMonitor m, NormalizedPath imagePath )
        {
            NormalizedPath tempPath = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            Directory.CreateDirectory( tempPath );
            ZipFile.ExtractToDirectory( imagePath, tempPath );
            return TestUniverse.Create( m, tempPath, imagePath.LastPart.Replace( ".zip", "" ) );

        }


        public static TestUniverse InstantiateAndGenerateImageIfNeeded(
            IActivityMonitor m,
            Action parentImageGenerator,
            string arbitraryCallName = null,
            [CallerMemberName] string callerMemberName = null )
        {
            string ourImageName = arbitraryCallName ?? callerMemberName;
            NormalizedPath generatedPreviousImagePath = GetImagePath( parentImageGenerator.Method.Name, false, true );
            if( !File.Exists( generatedPreviousImagePath ) )
            {
                parentImageGenerator();
            }
            if( !File.Exists( generatedPreviousImagePath ) )
            {
                throw new InvalidOperationException( "The parent image generator did not generated the expected image." );
            }
            //The previous image should now exist
            var ourImagePath = GetImagePath( ourImageName, false, false );
            if( !File.Exists( ourImagePath ) )
            {
                File.Copy( generatedPreviousImagePath, ourImagePath );
            }
            return InstantiateImage( m, false, ourImageName );
        }

        public const string PlaceHolderString = "PLACEHOLDER_CKLI_TESTS";


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

        public static bool CompareImages( string imageAPath, string imageBPath )
        {
            using( var imageA = ZipFile.OpenRead( imageAPath ) )
            using( var imageB = ZipFile.OpenRead( imageBPath ) )
            {
                var comparer = new ZipEntryComparer();
                var imageAItems = imageA.Entries.Except( imageB.Entries, comparer ).ToList();
                var imageBItems = imageB.Entries.Except( imageA.Entries, comparer ).ToList();
            }
            return true;
        }
    }
}
