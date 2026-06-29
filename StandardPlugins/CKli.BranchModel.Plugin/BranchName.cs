using CK.Core;
using CKli.Core;
using System;
using System.Diagnostics;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Branch in the <see cref="BranchModelPlugin.BranchNamespace"/>.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class BranchName
{
    readonly string _name;
    string? _devName;
    readonly BranchName? _parent;
    readonly int _index;
    internal readonly int _ltsPrefixLength;
    readonly BranchLinkType _linkType;

    internal BranchName( int ltsPrefixLength, BranchLinkType linkType, string name, int index, BranchName? parent )
    {
        _index = index;
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
    /// This follows the same pattern as the <see cref="CKli.Core.Repo.Index"/>: the <see cref="BranchModelInfo"/> uses this
    /// to associate the corresponding <see cref="HotBranch"/> in each repo.
    /// </summary>
    public int Index => _index;

    /// Gets the parent branch name or null if this is the <see cref="BranchNamespace.Root"/>.
    /// </summary>
    public BranchName? Parent => _parent;

    /// <summary>
    /// Gets the link type that describes the relationships regarding the <see cref="ParentIndex"/>.
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
            if( p ==  b ) return true;
            p = p.Parent;
        }
        return false;
    }

    /// <summary>
    /// Returns the <see cref="Name"/>.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name;

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

