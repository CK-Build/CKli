using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// The package directives of the "Deployment/GenerateAssets.cs" file-based app:
/// <list type="bullet">
///   <item>
///     "#:package Name@Version" (restored by "dotnet" for the script) are the "second class" package references of a
///     solution (see <see cref="GitSolutionContent.ScriptConsumed"/>).
///   </item>
///   <item>
///     "//ck-package Name@Version" (a line comment: "dotnet" ignores it, nothing is restored for the script) are first
///     class package references (they are <see cref="GitSolutionContent.Consumed"/>): the packages that the deployment
///     adds to an App and that end up inside the published application.
///   </item>
/// </list>
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

    // "#:package Name@Version", "#:package Name" (no version: centrally managed), "//ck-package Name@Version".
    // The line end "\r" is excluded from the version so that a rewrite keeps the line endings.
    static readonly Regex _rPackage = new Regex( @"^(?<prefix>[ \t]*(?<kind>#:package|//ck-package)[ \t]+(?<id>[^@\s]+))(?:@(?<version>[^\s]*))?(?<suffix>[ \t]*)\r?$",
                                                 RegexOptions.Multiline | RegexOptions.CultureInvariant );

    /// <summary>
    /// Calls <paramref name="collector"/> for each "#:package Name@Version" or "//ck-package Name@Version" directive
    /// whose version is a valid <see cref="SVersion"/>.
    /// <list type="bullet">
    ///   <item>
    ///     A "#:package" without version is skipped (its version is centrally managed, a "Directory.Packages.props"
    ///     carries it), a floating or invalid version is warned and skipped.
    ///   </item>
    ///   <item>
    ///     A "//ck-package" without version or with an invalid one is an error: nothing else can give its version.
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="text">The script text.</param>
    /// <param name="collector">
    /// The collector, called with the package identifier, its version and whether the directive is a first class
    /// "//ck-package". Returning false stops and returns false.
    /// </param>
    /// <returns>True on success, false on error or if the collector returned false.</returns>
    public static bool ReadPackages( IActivityMonitor monitor, string text, Func<string, SVersion, bool, bool> collector )
    {
        foreach( Match m in _rPackage.Matches( text ) )
        {
            var v = m.Groups["version"];
            var packageId = m.Groups["id"].Value;
            bool isFirstClass = IsFirstClass( m );
            if( !v.Success )
            {
                if( !isFirstClass ) continue;
                monitor.Error( $"Invalid '//ck-package {packageId}' in '{FilePath}': a version is required ('//ck-package {packageId}@<version>')." );
                return false;
            }
            if( !SVersion.TryParse( v.Value, out var version ) )
            {
                if( isFirstClass )
                {
                    monitor.Error( $"Invalid '//ck-package {packageId}@{v.Value}' in '{FilePath}': the version is not a valid version." );
                    return false;
                }
                monitor.Warn( $"""
                    Ignoring '#:package {packageId}@{v.Value}' in '{FilePath}': the version is not a valid version.
                    A floating version takes the highest version of any feed, not the one of the World.
                    """ );
                continue;
            }
            if( !collector( packageId, version, isFirstClass ) ) return false;
        }
        return true;
    }

    static bool IsFirstClass( Match m ) => m.Groups["kind"].Value[0] == '/';

    /// <summary>
    /// Reads the first class "//ck-package Name@Version" directives of the script of a commit (empty when the commit
    /// has no script). A build must add them to its consumed packages: "dotnet package list" doesn't see them.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="commit">The commit to read.</param>
    /// <param name="packages">The first class packages on success.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool ReadFirstClassPackages( IActivityMonitor monitor, Commit commit, out ImmutableArray<PackageInstance> packages )
    {
        packages = [];
        if( commit[FilePath]?.Target is not Blob blob ) return true;
        var b = ImmutableArray.CreateBuilder<PackageInstance>();
        if( !ReadPackages( monitor, blob.GetContentText(), ( packageId, version, isFirstClass ) =>
            {
                if( isFirstClass ) b.Add( new PackageInstance( packageId, version ) );
                return true;
            } ) )
        {
            return false;
        }
        packages = b.ToImmutable();
        return true;
    }

    /// <summary>
    /// Rewrites the version of the "#:package Name@Version" and "//ck-package Name@Version" directives that
    /// <paramref name="update"/> maps to another version. The rest of the text (and its line endings) is unchanged.
    /// </summary>
    /// <param name="text">The script text.</param>
    /// <param name="update">
    /// Called with the package identifier, its current version and whether the directive is a first class "//ck-package".
    /// Returns the new version or null to leave the directive as it is.
    /// </param>
    /// <returns>The updated text (equal to <paramref name="text"/> when nothing changed).</returns>
    public static string UpdatePackages( string text, Func<string, SVersion, bool, SVersion?> update )
    {
        return _rPackage.Replace( text, m =>
        {
            var v = m.Groups["version"];
            if( v.Success
                && SVersion.TryParse( v.Value, out var from )
                && update( m.Groups["id"].Value, from, IsFirstClass( m ) ) is SVersion to
                && to != from )
            {
                return m.Value.Substring( 0, v.Index - m.Index ) + to.ToString() + m.Value.Substring( v.Index + v.Length - m.Index );
            }
            return m.Value;
        } );
    }
}
