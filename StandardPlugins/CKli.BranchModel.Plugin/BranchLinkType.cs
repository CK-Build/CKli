using CK.Core;
using System;
using System.IO;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Defines the type of link between a <see cref="BranchName"/> and its more stable parent.
/// </summary>
public enum BranchLinkType
{
    /// <summary>
    /// Unknown or non applicable.
    /// </summary>
    None,

    /// <summary>
    /// Manual, no propagation at all ("|✋"): the "dev/" child branch must be manually updated.
    /// </summary>
    Manual,

    /// <summary>
    /// Restricted propagation ("|>"): the "dev/" child branch is synchronized with the parent branch (a stable or a prerelease
    /// must be built on the parent branch to impact the child).
    /// <para>
    /// Commits of stable or prerelease versions are merged into the "dev/" child branch, CI builds of the parent branch are ignored. 
    /// </para>
    /// </summary>
    Release,

    /// <summary>
    /// This is the default link ("->"): the "dev/" child branch is synchronized with the "dev/" parent branch but only on built commits
    /// (a build or build --ci must be have been done on the parent branch to impact the child).
    /// <para>
    /// All versioned commits are merged into the "dev/" child branch. 
    /// </para>
    /// </summary>
    CI,

    /// <summary>
    /// Full link ("=>"): the "dev/" child branch is synchronized with the "dev/" parent branch.
    /// </summary>
    Full
}

/// <summary>
/// Extends <see cref="BranchLinkType"/>.
/// </summary>
public static class BranchLinkTypeExtensions
{
    /// <summary>
    /// Gets the string associate to link type.
    /// </summary>
    /// <param name="linkType">This type.</param>
    /// <returns>The string.</returns>
    public static string ToCodeString( this BranchLinkType linkType ) => linkType switch
    {
        BranchLinkType.Full => "=>",
        BranchLinkType.CI => "->",
        BranchLinkType.Release => "|>",
        BranchLinkType.Manual => "|✋",
        _ => ""
    };

    /// <summary>
    /// Tries to match and forward the code for a <see cref="BranchLinkType"/>.
    /// </summary>
    /// <param name="h">This head.</param>
    /// <param name="t">The matched type.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryMatchLinkTypeCode( this ref ReadOnlySpan<char> h, out BranchLinkType t )
    {
        var savedH = h;
        t = BranchLinkType.None;
        if( h.TryMatch( '|' ) )
        {
            if( h.TryMatch('>') )
            {
                t = BranchLinkType.Release;
                return true;
            }
            if( h.TryMatch( '✋' ) )
            {
                t = BranchLinkType.Manual;
                return true;
            }
        }
        else if( h.TryMatch( "=>" ) )
        {
            t = BranchLinkType.Full;
            return true;
        }
        else if( h.TryMatch( "->" ) )
        {
            t = BranchLinkType.CI;
            return true;
        }
        h = savedH;
        return false;
    }



    /// <summary>
    /// Calls <see cref="TryMatchLinkType(ref ReadOnlySpan{char}, out BranchLinkType)"/> and
    /// throws a <see cref="InvalidDataException"/> on failure.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <returns>The link type (except <see cref="BranchLinkType.None"/>).</returns>
    public static BranchLinkType ParseLinkType( ReadOnlySpan<char> s )
    {
        var h = s;
        return TryMatchLinkType( ref h, out var t ) && h.IsEmpty
                ? t
                : throw new InvalidDataException( "Expected 'Manual', 'Release', 'CI', 'Full'." );
    }

    /// <summary>
    /// Tries to parse "Manual", "Release", "CI", "Full".
    /// </summary>
    /// <param name="h">This head.</param>
    /// <param name="t">The matched type.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryMatchLinkType( this ref ReadOnlySpan<char> h, out BranchLinkType t )
    {
        if( h.TryMatch( "ci", StringComparison.OrdinalIgnoreCase ) )
        {
            t = BranchLinkType.CI;
            return true;
        }
        if( h.TryMatch( "full", StringComparison.OrdinalIgnoreCase ) )
        {
            t = BranchLinkType.Full;
            return true;
        }
        if( h.TryMatch( "release", StringComparison.OrdinalIgnoreCase ) )
        {
            t = BranchLinkType.Release;
            return true;
        }
        if( h.TryMatch( "manual", StringComparison.OrdinalIgnoreCase ) )
        {
            t = BranchLinkType.Manual;
            return true;
        }
        t = BranchLinkType.None;
        return false;
  }
}
