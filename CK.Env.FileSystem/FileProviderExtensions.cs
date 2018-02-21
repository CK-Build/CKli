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

        /// <summary>
        /// Gets a <see cref="FileProviderContentInfo"/> for a given <paramref name="root"/>.
        /// </summary>
        /// <param name="this">This file provider.</param>
        /// <param name="root">Root path.</param>
        /// <returns>A new <see cref="FileProviderContentInfo"/>.</returns>
        public static FileProviderContentInfo GetContentInfo( this IFileProvider @this, NormalizedPath root ) => new FileProviderContentInfo( @this, root );

    }
}
