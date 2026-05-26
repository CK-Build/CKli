using Microsoft.Extensions.FileProviders;
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
    /// Reads the content on this file as raw bytes.
    /// </summary>
    /// <param name="info">This file info.</param>
    /// <returns>The file content.</returns>
    public static byte[] ReadAsBytes( this IFileInfo info )
    {
        using( var s = info.CreateReadStream() )
        {
            var content = new byte[info.Length];
            s.ReadExactly( content );
            return content;
        }
    }

}
