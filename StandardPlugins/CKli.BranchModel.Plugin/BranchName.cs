using CK.Core;
using CKli.Core;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Immutable branch in the <see cref="BranchModelPlugin.BranchNamespace"/>.
/// <para>
/// The <see cref="Name"/> starts with the "<see cref="WorldName.LTSName"/>/" when in a LTS world.
/// </para>
/// <para>
/// Use <see cref="Match(SVersion)"/> to test whether a <see cref="SVersion"/> is bound to a branch
/// (avoid using <see cref="SVersion.BranchName"/>) and use <see cref="BranchNamespace.Find(SVersion)"/>
/// to find a branch from a version.
/// </para>
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class BranchName : IEquatable<BranchName>
{
    readonly string _name;
    string? _devName;
    readonly BranchName? _parent;
    readonly int _index;
    readonly CSVersionKind _versionKind;
    internal readonly int _ltsPrefixLength;
    readonly BranchLinkType _linkType;

    internal BranchName( int ltsPrefixLength, BranchLinkType linkType, string name, int index, CSVersionKind versionKind, BranchName? parent )
    {
        _index = index;
        _versionKind = versionKind;
        _parent = parent;
        _ltsPrefixLength = ltsPrefixLength;
        _linkType = linkType;
        _name = name;
    }

    /// <summary>
    /// Gets the branch name.
    /// This starts with the <see cref="WorldName.LTSName"/> in a LTS world.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the "dev/<see cref="Name"/>" branch name.
    /// This starts with the <see cref="WorldName.LTSName"/> in a LTS world.
    /// </summary>
    public string DevName => _devName ??= ToDevBranchName( _ltsPrefixLength, _name );

    /// <summary>
    /// Gets the <see cref="Name"/> without the "<see cref="WorldName.LTSName"/>/" prefix: this is the form that
    /// the BranchModel configuration holds (the MainLine attribute and the &lt;Explo&gt; Name and Parent attributes).
    /// <para>
    /// The prefix must not be written: <see cref="BranchNamespace"/> prepends it when it reads the configuration,
    /// and its parser rejects a name that starts with the '@' of a <see cref="WorldName.LTSName"/>. This is the
    /// same string as <see cref="Name"/> in the default world.
    /// </para>
    /// </summary>
    public string ConfigurationName => _ltsPrefixLength == 0 ? _name : _name.Substring( _ltsPrefixLength );

    /// <summary>
    /// Gets the index in <see cref="BranchNamespace.Branches"/>.
    /// <para>
    /// This follows the same pattern as the <see cref="Repo.Index"/>: the <see cref="BranchModelInfo"/> uses this
    /// to associate the corresponding <see cref="HotBranch"/> in each repo.
    /// </para>
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// Gets the parent branch name or null if this is the <see cref="BranchNamespace.Root"/>.
    /// </summary>
    public BranchName? Parent => _parent;

    /// <summary>
    /// Gets whether this is the <see cref="BranchNamespace.Root"/>.
    /// </summary>
    [MemberNotNullWhen(false, nameof( Parent ), nameof( _parent ) )]
    public bool IsRoot => _parent == null;

    /// <summary>
    /// Gets the link type that describes the relationships with the <see cref="Parent"/>.
    /// <para>
    /// This is <see cref="BranchLinkType.None"/> when <see cref="IsRoot"/> is true.
    /// </para>
    /// </summary>
    public BranchLinkType LinkType => _linkType;

    /// <summary>
    /// Gets the <see cref="CSVersionKind"/>. Never <see cref="CSVersionKind.None"/>.
    /// </summary>
    public CSVersionKind VersionKind => _versionKind;

    /// <summary>
    /// Gets the exploratory name when <see cref="VersionKind"/> is <see cref="CSVersionKind.Exploratory"/>,
    /// the empty span otherwise: this is the <see cref="Name"/> without its <see cref="WorldName.LTSName"/>
    /// and "explo/" prefixes.
    /// <para>
    /// This is the <see cref="SVersion.ExploratoryName"/> of the versions of this branch.
    /// </para>
    /// </summary>
    public ReadOnlySpan<char> ExploratoryName => _versionKind is CSVersionKind.Exploratory
                                                    ? _name.AsSpan( _ltsPrefixLength + 6 )
                                                    : default;

    /// <summary>
    /// Gets whether this branch corresponds to the <paramref name="version"/>.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <returns>True if the version corresponds to this branch name.</returns>
    public bool Match( SVersion version )
    {
        return version.VersionKind is CSVersionKind.Exploratory
                ? ExploratoryName.Equals( version.ExploratoryName, StringComparison.Ordinal )
                : version.VersionKind == _versionKind;
    }

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
    /// <param name="csPrerelease">The standard prerelease name if this is not a "explo/" branch. <see cref="CSVersionKind.None"/> otherwise.</param>
    /// <returns>Whether <paramref name="branchName"/> is syntactically valid.</returns>
    public static bool TryParseBranchName( IActivityMonitor monitor, string branchName, out CSVersionKind csPrerelease )
    {
        var h = branchName.AsSpan();
        if( h.TryMatch( "explo/", StringComparison.Ordinal ) )
        {
            csPrerelease = CSVersionKind.None;
            if( !BranchNamespace.MatchBranchSegment( ref h, out var exploName ) || !h.SkipWhiteSpaces() || h.Length != 0 )
            {
                monitor.Error( $"Invalid '{branchName}'. Segment '{branchName.AsSpan( 6 )}' must be a lowercase ASCII identifier (which may contain dash '-' or underscore '_')" );
                return false;
            }
            if( SVersion.IsReservedExploratoryName( exploName ) )
            {
                monitor.Error( $"Invalid '{branchName}'. {BranchNamespace.ReservedExploratoryNameError}" );
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

