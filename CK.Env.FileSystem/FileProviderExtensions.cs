using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;

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
