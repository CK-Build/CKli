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
        public static string ReadAsText( this IFileInfo @this )
        {
            using( var s = @this.CreateReadStream() )
            using( var t = new StreamReader( s ) )
            {
                return t.ReadToEnd().NormalizeEOL();
            }
        }
    }
}
