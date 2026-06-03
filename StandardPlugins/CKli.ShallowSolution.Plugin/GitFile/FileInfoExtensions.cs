using CK.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.IO;
using System.IO;
using System.Text;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Extends IFileInfo.
/// </summary>
public static class FileInfoExtensions
{
    /// <summary>
    /// Reads the content on this file as a text (using <see cref="Encoding.UTF8"/>).
    /// </summary>
    /// <param name="info">This file info.</param>
    /// <returns>The file content as a string.</returns>
    public static string ReadAsText( this IFileInfo info )
    {
        using( var s = info.CreateReadStream() )
        using( var r = new StreamReader( s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true ) )
        {
            return r.ReadToEnd();
        }
    }

    /// <summary>
    /// Reads the content of this file this file as raw bytes.
    /// </summary>
    /// <param name="info">This file info.</param>
    /// <returns>The file content as a recyclable memory stream.</returns>
    public static byte[] ReadAsBytes( this IFileInfo info )
    {
        // We CANNOT rely on the IFileInfo.Length here because of the "autocrlf" (or any
        // other rewrite of the stream).
        // We use the RecyclableMemoryStream for its memory pool but allocates a plain
        // final buffer... Not clever...
        using( var s = info.CreateReadStream() )
        using( var content = Util.RecyclableStreamManager.GetStream() )
        {
            s.CopyTo( content );
            var r = new byte[content.Length];
            content.WriteTo( r );
            return r;
        }
    }

}
