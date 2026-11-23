using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Computes the viewers that "ckli log" tries on Linux.
/// </summary>
public static class LinuxLogViewer
{
    /// <summary>
    /// Gets the ordered /bin/sh scripts to try to display a log file. Each script receives the file path as "$1".
    /// When all of them fail (or when the list is empty), the file must be written to the standard output.
    /// </summary>
    /// <param name="ckliPager">The CKLI_PAGER environment variable value.</param>
    /// <param name="pager">The PAGER environment variable value.</param>
    /// <param name="outputRedirected">True when the standard output is redirected: no viewer must be used.</param>
    /// <returns>The scripts to try, in order.</returns>
    public static string[] GetScripts( string? ckliPager, string? pager, bool outputRedirected )
    {
        if( outputRedirected ) return [];
        var scripts = new List<string>();
        if( !string.IsNullOrWhiteSpace( ckliPager ) ) scripts.Add( $"{ckliPager.Trim()} \"$1\"" );
        if( !string.IsNullOrWhiteSpace( pager ) ) scripts.Add( $"{pager.Trim()} \"$1\"" );
        scripts.Add( "xdg-open \"$1\" >/dev/null 2>&1" );
        scripts.Add( "less \"$1\"" );
        return scripts.ToArray();
    }
}
