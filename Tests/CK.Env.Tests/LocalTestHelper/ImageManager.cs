using CK.Text;
using System;
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

        NormalizedPath GetImagePath( string imageName, bool tempImage = false )
        {
            return (tempImage ? BuildedImageFolder : TestsUniverseFolder).AppendPart( imageName + ".zip" );
        }

        /// <summary>
        /// Instantiate an image into a <see cref="TestUniverse"/> with a working folder.
        /// </summary>
        /// <param name="imageName">Image to instantiate.</param>
        /// <param name="fromTempImage">False if the image is stored in <see cref="TestsUniverseFolder"/> or the <see cref="ImageManager"/> temp folder.</param>
        /// <returns></returns>
        public TestUniverse InstantiateImage( bool fromTempImage, string arbitraryCallName = null, [CallerMemberName] string callerMemberName = null )
        {
            NormalizedPath tempPath = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            Directory.CreateDirectory( tempPath );
            string imageName = arbitraryCallName ?? callerMemberName;
            ZipFile.ExtractToDirectory( GetImagePath( imageName, fromTempImage ), tempPath );
            return TestUniverse.Create( this, tempPath, imageName );
        }

        /// <summary>
        /// Generate an image from a <see cref="TestUniverse"/> and will be stored in the <see cref="ImageManager"/> temp path.
        /// </summary>
        /// <param name="universe"></param>
        public void BuildImage( TestUniverse universe )
        {
            foreach( StackConfig config in universe.Configs.Select( p => p.Value ) )
            {
                config.PlaceHolderSwap( false );
                config.Save();
            }

            ZipFile.CreateFromDirectory( universe.TempPath, _tempGeneratedImagePath.AppendPart( universe.ImageName ) );

            foreach( StackConfig config in universe.Configs.Select( p => p.Value ) )
            {
                config.PlaceHolderSwap( false );
                config.Save();
            }
        }

        public void Dispose()
        {
            FileHelper.RawDeleteLocalDirectory( TestHelper.Monitor, _tempGeneratedImagePath );
        }
    }
}
