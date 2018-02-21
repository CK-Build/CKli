using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CK.Env
{
    public static class FileInfoExtensions
    {
        /// <summary>
        /// Reads the content of this file as a string.
        /// Uses standard automatic encoding detection.
        /// </summary>
        /// <param name="this">This file info.</param>
        /// <returns>The text content.</returns>
        public static string ReadAsText( this IFileInfo @this )
        {
            using( var s = @this.CreateReadStream() )
            using( var t = new StreamReader( s ) )
            {
                return t.ReadToEnd().NormalizeEOL();
            }
        }

        /// <summary>
        /// Checks content equality. This <see cref="IFileInfo"/> and <paramref name="file"/>
        /// must both be actual files, not directories.
        /// </summary>
        /// <param name="this">This file info.</param>
        /// <param name="file">The file to check.</param>
        /// <returns>True of the files have exactly the same content, false otherwise.</returns>
        public static bool ContentEquals( this IFileInfo @this, IFileInfo file )
        {
            if( @this.IsDirectory ) throw new ArgumentException( "Must be a file.", nameof( @this ) );
            if( file == null ) throw new ArgumentNullException( nameof( file ) );
            if( file.IsDirectory ) throw new ArgumentException( "Must be a file.", nameof( file ) );
            if( @this.Length != file.Length ) return false;
            using( var s = @this.CreateReadStream() )
            using( var d = file.CreateReadStream() )
            {
                int read;
                for(; ; )
                {
                    if( s.ReadByte() != (read = d.ReadByte()) ) return false;
                    if( read == -1 ) return true;
                }
            }
        }



    }
}
