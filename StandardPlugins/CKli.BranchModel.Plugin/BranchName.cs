using CK.Core;
using CKli.Core;
using System;
using System.Data;
using System.Diagnostics;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Branch in the <see cref="BranchModelPlugin.BranchNamespace"/>.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class BranchName : IEquatable<BranchName>
{
    readonly string _name;
    string? _devName;
    readonly BranchName? _parent;
    readonly int _index;
    readonly CSVersionKind _kind;
    internal readonly int _ltsPrefixLength;
    readonly BranchLinkType _linkType;

    internal BranchName( int ltsPrefixLength, BranchLinkType linkType, string name, int index, CSVersionKind kind, BranchName? parent )
    {
        _index = index;
        _kind = kind;
        _parent = parent;
        _ltsPrefixLength = ltsPrefixLength;
        _linkType = linkType;
        _name = name;
    }

    /// <summary>
    /// Gets the branch name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the "dev/<see cref="Name"/>" branch name.
    /// </summary>
    public string DevName => _devName ??= ToDevBranchName( _ltsPrefixLength, _name );

    /// <summary>
    /// Gets the index in <see cref="BranchNamespace.Branches"/>.
    /// <para>
    /// This follows the same pattern as the <see cref="Repo.Index"/>: the <see cref="BranchModelInfo"/> uses this
    /// to associate the corresponding <see cref="HotBranch"/> in each repo.
    /// </para>
    /// </summary>
    public int Index => _index;

    /// Gets the parent branch name or null if this is the <see cref="BranchNamespace.Root"/>.
    /// </summary>
    public BranchName? Parent => _parent;

    /// <summary>
    /// Gets the link type that describes the relationships with the <see cref="Parent"/>.
    /// </summary>
    public BranchLinkType LinkType => _linkType;

    /// <summary>
    /// Gets whether a branch name is below this one.
    /// </summary>
    /// <param name="b">The potential child.</param>
    /// <returns>True if this is a parent of <paramref name="b"/>.</returns>
    public bool HasChild( BranchName b )
    {
        var p = b.Parent;
        while( p != null )
        {
            if( p == this ) return true;
            p = p.Parent;
        }
        return false;
    }

    /// <summary>
    /// Gets whether this branch corresponds to the <paramref name="version"/>.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>True if the version corresponds to this branch name.</returns>
    public bool Match( SVersion version )
    {
        return version.VersionKind is CSVersionKind.Exploratory
                ? _name.AsSpan( _ltsPrefixLength + 6 ).Equals( version.ExploratoryName, StringComparison.Ordinal )
                : version.VersionKind == _kind;
    }

    /// <summary>
    /// Two branch names are equal if and only if their <see cref="Name"/>, <see cref="LinkType"/> and <see cref="Parent"/>'s name are the same.
    /// </summary>
    /// <param name="other">The other branch name.</param>
    /// <returns>True if this is equal to other.</returns>
    public bool Equals( BranchName? other ) => other != null && _name == other._name && _linkType == other._linkType && _parent?.Name == other._parent?.Name;

    /// <summary>
    /// See <see cref="Equals(BranchName?)"/>.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns><c>true</c> if the specified object is equal to the current object; otherwise, <c>false</c>.</returns>
    public override bool Equals( object? obj ) => Equals( obj as BranchName );

    /// <summary>
    /// Relies on <see cref="Name"/>, <see cref="LinkType"/> and <see cref="Parent"/>'s name.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine( _name, _linkType, _parent?.Name );

    /// <summary>
    /// Gets this branch name subordinated to its parent as it appears in <see cref="BranchNamespace.GetDisplayTree()"/>.
    /// </summary>
    /// <returns></returns>
    public string ToParentedString()
    {
        return _parent == null
                    ? _name
                    : $"""
                      {_parent.Name}
                        {_linkType.ToCodeString()} {_name}
                      """;
    }

    /// <summary>
    /// Returns the <see cref="Name"/>.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name;

    /// <summary>
    /// Tries to parse a "explo/" or "zulu"... "alpha" branch name and logs an error on failure.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branchName">The branch name.</param>
    /// <param name="csPrerelease">The standard prerelease name if this is not a "explo/" branch.</param>
    /// <returns>Whether <paramref name="branchName"/> is syntactically valid.</returns>
    public static bool TryParseBranchName( IActivityMonitor monitor, string branchName, out CSVersionKind csPrerelease )
    {
        var h = branchName.AsSpan();
        if( h.TryMatch( "explo/", StringComparison.Ordinal ) )
        {
            csPrerelease = CSVersionKind.None;
            if( !BranchNamespace.MatchBranchSegment( ref h, out _ ) || !h.SkipWhiteSpaces() || h.Length != 0 )
            {
                monitor.Error( $"Invalid '{branchName}'. Segment '{branchName.AsSpan( 6 )}' must be a lowercase ASCII identifier (which may contain dash '-' or underscore '_')" );
                return false;
            }
        }
        else if( !CSVersionKindExtensions.TryParse( h, out csPrerelease, StringComparison.Ordinal ) )
        {
            monitor.Error( $"""
                Invalid branch name '{branchName}'. It must be a Conformant SVersion prerelease name ('alpha', 'bravo', ...'zulu') or an exploratory 'explo/name' branch name.
                """ );
            return false;
        }
        return true;
    }

    static string ToDevBranchName( int ltsPrefixLength, string name )
    {
        if( ltsPrefixLength == 0 )
        {
            return name.StartsWith( "dev/", StringComparison.OrdinalIgnoreCase )
                    ? name.StartsWith( "dev/", StringComparison.Ordinal )
                        ? name
                        : $"dev/{name.AsSpan( 4 )}"
                    : $"dev/{name}";
        }
        var sName = name.AsSpan( ltsPrefixLength );
        return sName.StartsWith( "dev/", StringComparison.OrdinalIgnoreCase )
                ? sName.StartsWith( "dev/", StringComparison.Ordinal )
                    ? name
                    : $"{name.AsSpan(0,ltsPrefixLength)}dev/{sName.Slice( 4 )}"
                : $"{name.AsSpan( 0, ltsPrefixLength )}dev/{sName}";

    }

}

