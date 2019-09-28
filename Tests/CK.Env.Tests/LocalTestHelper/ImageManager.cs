using CK.Core;
using CK.Text;
using System;
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
    public class ImageManager : IDisposable
    {
        readonly NormalizedPath _tempGeneratedImagePath;

        ImageManager( NormalizedPath tempPath )
        {
            _tempGeneratedImagePath = tempPath;
        }

        public static ImageManager Create()
        {
            var im = new ImageManager( Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() ) );
            Directory.CreateDirectory( im._tempGeneratedImagePath );
            Directory.CreateDirectory( im.BuildedImageFolder );
            return im;
        }

        /// <summary>
        /// Path to the folder storing the builded **Images**.
        /// </summary>
        NormalizedPath BuildedImageFolder => _tempGeneratedImagePath.AppendPart( "Images" );

        /// <summary>
        /// Path to the folder storing the **Universe Zips** used for the Tests.
        /// </summary>
        static NormalizedPath TestsUniverseFolder => TestHelper.TestProjectFolder.AppendPart( "UniverseZips" );

        /// <summary>
        /// 
        /// </summary>
        /// <param name="imageName"></param>
        /// <param name="tempImage"></param>
        /// <param name="buildedOrBase"><see langword="true"/> for builded, <see langword="false"/> for a base image.</param>
        /// <returns></returns>
        NormalizedPath GetImagePath( string imageName, bool tempImage, bool buildedOrBase )
        {
            return (tempImage ? BuildedImageFolder : TestsUniverseFolder).AppendPart( imageName + (buildedOrBase ? "#builded" : "") + ".zip" );
        }

        /// <summary>
        /// Instantiate an image into a <see cref="TestUniverse"/> with a working folder.
        /// </summary>
        /// <param name="imageName">Image to instantiate.</param>
        /// <param name="fromTempImage">False if the image is stored in <see cref="TestsUniverseFolder"/> or the <see cref="ImageManager"/> temp folder.</param>
        /// <returns></returns>
        public TestUniverse InstantiateImage(IActivityMonitor m, bool fromTempImage, string arbitraryCallName = null, [CallerMemberName] string callerMemberName = null )
        {
            NormalizedPath tempPath = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            Directory.CreateDirectory( tempPath );
            string imageName = arbitraryCallName ?? callerMemberName;
            ZipFile.ExtractToDirectory( GetImagePath( imageName, fromTempImage, false ), tempPath );
            return TestUniverse.Create( m, this, tempPath, imageName );
        }

        public TestUniverse InstantiateAndGenerateImageIfNeeded( IActivityMonitor m, bool generateAndUseParentImage, Action<bool> parentImageGenerator, string arbitraryCallName = null, [CallerMemberName] string callerMemberName = null )
        {
            string ourImageName = arbitraryCallName ?? callerMemberName;
            if( generateAndUseParentImage )
            {
                NormalizedPath generatedPreviousImagePath = GetImagePath( parentImageGenerator.Method.Name, true, true );
                if( !File.Exists( generatedPreviousImagePath ) )
                {
                    parentImageGenerator( true );
                }
                if( !File.Exists( generatedPreviousImagePath ) )
                {
                    throw new InvalidOperationException( "The parent image generator did not generated the expected image." );
                }
                //The previous image should now exist
                var ourImagePath = GetImagePath( ourImageName, true, false );
                if( !File.Exists( ourImagePath ) )
                {
                    File.Copy( generatedPreviousImagePath, ourImagePath );
                }
            }
            return InstantiateImage(m, generateAndUseParentImage, ourImageName );
        }

        public const string PlaceHolderString = "PLACEHOLDER_CKLI_TESTS";

        /// <summary>
        /// Generate an image from a <see cref="TestUniverse"/> and will be stored in the <see cref="ImageManager"/> temp path.
        /// </summary>
        /// <param name="universe"></param>
        public void BuildImage(IActivityMonitor m, TestUniverse universe )
        {
            int cnt = universe.SwapAllGitOriginPlaceholders(m, universe.TempPath, PlaceHolderString );
            foreach( StackConfig config in universe.Configs.Select( p => p.Value ) )
            {
                config.PlaceHolderSwap( false, PlaceHolderString );
                config.Save();
            }

            ZipFile.CreateFromDirectory( universe.TempPath, GetImagePath( universe.ImageName, true, true) );

            foreach( StackConfig config in universe.Configs.Select( p => p.Value ) )
            {
                config.PlaceHolderSwap( false, PlaceHolderString );
                config.Save();
            }
        }

        public void Dispose()
        {
            FileHelper.RawDeleteLocalDirectory( TestHelper.Monitor, _tempGeneratedImagePath );
        }
    }
}
