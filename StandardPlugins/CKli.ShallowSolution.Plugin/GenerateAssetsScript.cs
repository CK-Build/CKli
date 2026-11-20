using CK.Core;
using System;
using System.Text.RegularExpressions;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// The "#:package" directives of the "Deployment/GenerateAssets.cs" file-based app: they are the "second class"
/// package references of a solution (see <see cref="GitSolutionContent.ScriptConsumed"/>).
/// <para>
/// Only this file is considered: the other file-based apps of a repository are not part of its build.
/// </para>
/// </summary>
public static class GenerateAssetsScript
{
    /// <summary>
    /// The path of the script, relative to the repository's working folder.
    /// </summary>
    public const string FilePath = "Deployment/GenerateAssets.cs";

    // "#:package Name@Version", "#:package Name" (no version: centrally managed). The line end "\r" is excluded
    // from the version so that a rewrite keeps the line endings.
    static readonly Regex _rPackage = new Regex( @"^(?<prefix>[ \t]*#:package[ \t]+(?<id>[^@\s]+))(?:@(?<version>[^\s]*))?(?<suffix>[ \t]*)\r?$",
                                                 RegexOptions.Multiline | RegexOptions.CultureInvariant );

    /// <summary>
    /// Calls <paramref name="collector"/> for each "#:package Name@Version" directive whose version is a valid
    /// <see cref="SVersion"/>. A directive without version is skipped (its version is centrally managed, a
    /// "Directory.Packages.props" carries it), a floating or invalid version is warned and skipped.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="text">The script text.</param>
    /// <param name="collector">The collector: returning false stops and returns false.</param>
    /// <returns>True on success, false if the collector returned false.</returns>
    public static bool ReadPackages( IActivityMonitor monitor, string text, Func<string, SVersion, bool> collector )
    {
        foreach( Match m in _rPackage.Matches( text ) )
        {
            var v = m.Groups["version"];
            if( !v.Success ) continue;
            var packageId = m.Groups["id"].Value;
            if( !SVersion.TryParse( v.Value, out var version ) )
            {
                monitor.Warn( $"""
                    Ignoring '#:package {packageId}@{v.Value}' in '{FilePath}': the version is not a valid version.
                    A floating version takes the highest version of any feed, not the one of the World.
                    """ );
                continue;
            }
            if( !collector( packageId, version ) ) return false;
        }
        return true;
    }

    /// <summary>
    /// Rewrites the version of the "#:package Name@Version" directives that <paramref name="update"/> maps
    /// to another version. The rest of the text (and its line endings) is unchanged.
    /// </summary>
    /// <param name="text">The script text.</param>
    /// <param name="update">Returns the new version or null to leave the directive as it is.</param>
    /// <returns>The updated text (equal to <paramref name="text"/> when nothing changed).</returns>
    public static string UpdatePackages( string text, Func<string, SVersion, SVersion?> update )
    {
        return _rPackage.Replace( text, m =>
        {
            var v = m.Groups["version"];
            if( v.Success
                && SVersion.TryParse( v.Value, out var from )
                && update( m.Groups["id"].Value, from ) is SVersion to
                && to != from )
            {
                return m.Value.Substring( 0, v.Index - m.Index ) + to.ToString() + m.Value.Substring( v.Index + v.Length - m.Index );
            }
            return m.Value;
        } );
    }
}
