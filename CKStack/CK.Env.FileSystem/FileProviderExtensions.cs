using CK.Core;
using Microsoft.Extensions.FileProviders;

namespace CK.Env
{
    public static class FileProviderExtensions
    {
        public static IFileInfo GetFileInfo( this IFileProvider @this, NormalizedPath path )
        {
            return @this.GetFileInfo( path.ToString() );
        }

        public static IDirectoryContents GetDirectoryContents( this IFileProvider @this, NormalizedPath path )
        {
            return @this.GetDirectoryContents( path.ToString() );
        }

    }
}
