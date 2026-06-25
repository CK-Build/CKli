using CK.Core;
using System;

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
    /// Manual, no propagation at all ("✋"): the "dev/" child branch must be manually updated.
    /// </summary>
    Manual,

    /// <summary>
    /// Restricted propagation ("|"): the "dev/" child branch is synchronized with the parent branch (a stable or a prerelease
    /// must be built on the parent branch to impact the child).
    /// <para>
    /// Commits of stable or prerelease versions are merged into the "dev/" child branch. 
    /// </para>
    /// </summary>
    Release,

    /// <summary>
    /// This is the default link ("->"): the "dev/" child branch is synchronized with the "dev/" parent branch but only on built commits
    /// (a build or build --ci must be done on the parent branch to impact the child).
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
        BranchLinkType.Release => "|",
        BranchLinkType.Manual => "✋",
        _ => ""
    };

    /// <summary>
    /// Tries to match and forward the code for a <see cref="BranchLinkType"/>.
    /// </summary>
    /// <param name="h">The head.</param>
    /// <param name="t">The matched type.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryMatchLinkTypeCode( this ref ReadOnlySpan<char> h, out BranchLinkType t )
    {
        t = BranchLinkType.None;
        if( h.TryMatch( '|' ) )
        {
            t = BranchLinkType.Release;
        }
        else if( h.TryMatch( '✋' ) )
        {
            t = BranchLinkType.Manual;
        }
        else
        {
            var savedH = h;
            bool full = h.TryMatch( '=' );
            if( !full && !h.TryMatch( '-' )
                || !h.TryMatch( '>' ) )
            {
                h = savedH;
                return false;
            }
            t = full ? BranchLinkType.Full : BranchLinkType.CI;
        }
        h.SkipWhiteSpaces();
        return true;
    }

}
