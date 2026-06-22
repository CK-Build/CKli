using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Captures the branches that makes the hot zone.
/// </summary>
public sealed partial class BranchNamespace
{
    readonly BranchName _root;
    readonly ImmutableArray<BranchName> _branches;
    readonly Dictionary<string, BranchName> _byName;

    internal BranchNamespace( string? ltsName,
                              string? sMainLine,
                              XElement? otherBranches )
    {
        Create( ltsName,
                sMainLine != null ? ParseMainLine( sMainLine ) : [("stable", BranchLinkType.None)],
                otherBranches,
                out _branches,
                out _byName );
        _root = _branches[0];
    }

    static void Create( string? ltsName,
                        IEnumerable<(string BranchName, BranchLinkType Link)> mainLine,
                        XElement? otherBranches,
                        out ImmutableArray<BranchName> branches,
                        out Dictionary<string, BranchName> byName )
    {
        var result = ImmutableArray.CreateBuilder<BranchName>();
        byName = new Dictionary<string, BranchName>();
        var e = mainLine.GetEnumerator();
        if( !e.MoveNext() )
        {
            throw new CKException( "Empty configuration." );
        }
        // root branch.
        var name = e.Current.BranchName;
        name = ltsName == null
                    ? name
                    : ltsName + '/' + name;
        var b = new BranchName( BranchLinkType.None, name, 0, null );
        result.Add( b );
        byName.Add( b.Name, b );
        while( e.MoveNext() )
        {
            name = e.Current.BranchName;
            name = ltsName == null
                        ? name
                        : ltsName + '/' + name;
            b = new BranchName( e.Current.Link, name, b.Index + 1, b );
            result.Add( b );
            byName.Add( b.Name, b );
        }
        int lastMainLineIndex = result.Count;
        if( otherBranches != null )
        {
            AddBranches( otherBranches.Elements( XNames.Branch ), ltsName, result, byName, parent: null );
        }
        branches = result.DrainToImmutable();


        static void AddBranches( IEnumerable<XElement> branches,
                                 string? ltsName,
                                 ImmutableArray<BranchName>.Builder result,
                                 Dictionary<string, BranchName> index,
                                 BranchName? parent )
        {
            foreach( var e in branches )
            {
                // The Parent name is required or rejected.
                var pAttr = e.Attribute( XNames.Parent );
                if( parent == null )
                {
                    var pName = (string?)pAttr;
                    parent = string.IsNullOrWhiteSpace( pName ) ? null : index.GetValueOrDefault( pName );
                    if( parent == null )
                    {
                        throw new CKException( $"""Unable to find Parent="{pName}" parent branch in BranchModel configuration.""" );
                    }
                }
                else if( pAttr != null )
                {
                    throw new CKException( $"""Unexpected Parent="..." attribute in BranchModel configuration (parent is '{parent}' branch).""" );
                }
                // Handling Name="<linkType>name".
                // LinkType is optional.
                // Currently defaults to None.
                BranchLinkType linkType = BranchLinkType.None;
                var name = (string?)e.Attribute( XNames.Name );
                if( string.IsNullOrWhiteSpace( name ) )
                {
                    throw new CKException( $"""Expected Name="..." attribute in BranchModel configuration.""" );
                }
                var h = name.AsSpan();
                name = MatchLinkTypeAndBranchName( ref h, ref linkType, expectLinkType: false );
                name = ltsName == null
                            ? name
                            : ltsName + '/' + name;
                if( index.ContainsKey( name ) )
                {
                    Throw.CKException( $"Duplicate branch name '{name}' in BranchModel configuration." );
                }
                var b = new BranchName( linkType, name, result.Count, parent );
                result.Add( b );
                index.Add( b.Name, b ); 
            }
        }
    }

    static List<(string BranchName, BranchLinkType Link)> ParseMainLine( string configuration )
    {
        var result = new List<(string BranchName, BranchLinkType Link)>();
        int count = 0;
        string? prevBranchName = null;
        ReadOnlySpan<char> h = configuration;
        while( h.SkipWhiteSpaces() && h.Length > 0 )
        {
            BranchLinkType linkType = BranchLinkType.None;
            string branchName = MatchLinkTypeAndBranchName( ref h, ref linkType, count > 0 );
            if( prevBranchName != null && prevBranchName.CompareTo( branchName, StringComparison.Ordinal ) <= 0 )
            {
                throw new CKException( $"""
                    Invalid branch name '{branchName}' in BranchModel configuration.
                    The branch name must be greater than '{prevBranchName}'.
                    """ );
            }
            prevBranchName = branchName;
            ++count;
            result.Add( (branchName, linkType) );
        }
        return result;

    }

    static bool MatchLinkType( ref ReadOnlySpan<char> h, out BranchLinkType t )
    {
        t = BranchLinkType.PreRelease;
        if( h.TryMatch( '|' ) )
        {
            t = h.TryMatch( '|' )
                    ? BranchLinkType.None
                    : BranchLinkType.Stable;
        }
        else
        {
            bool full = h.TryMatch( '=' );
            if( !full && !h.TryMatch( '-' )
                || !h.TryMatch( '>' ) )
            {
                return false;
            }
            t = full ? BranchLinkType.Full : BranchLinkType.PreRelease;
        }
        h.SkipWhiteSpaces();
        return true;
    }

    static string MatchLinkTypeAndBranchName( ref ReadOnlySpan<char> h, ref BranchLinkType linkType, bool expectLinkType )
    {
        if( expectLinkType && !MatchLinkType( ref h, out linkType ) )
        {
            throw new CKException( $"""
                    Unable to parse link type in BranchModel configuration.
                    Expected '||' (None), '|' (Stable), '->' (Prerelease) or '=>' (CI), got:
                    {h}
                    """ );
        }
        var e = ValidBranchName().EnumerateMatches( h );
        if( !e.MoveNext() )
        {
            throw new CKException( $"""
                    Unable to parse a branch name in BranchModel configuration.
                    Expected a lowercase ASCII identifier (which may contain dash '-' or underscore '_'), got:
                    {h}
                    """ );
        }
        Throw.DebugAssert( e.Current.Index == 0 );
        var sBranchName = h.Slice( 0, e.Current.Length );
        h = h.Slice( e.Current.Length );
        return new string( sBranchName );
    }

    [GeneratedRegex( "^[a-z][0-9a-z_-]+", RegexOptions.CultureInvariant )]
    private static partial Regex ValidBranchName();

    /// <summary>
    /// Gets the "stable" root branch name.
    /// </summary>
    public BranchName Root => _root;

    /// <summary>
    /// Gets the branches in increasing <see cref="BranchName.Index"/>.
    /// </summary>
    public ImmutableArray<BranchName> Branches => _branches;

    /// <summary>
    /// Gets an index of the branch names by their <see cref="BranchName.Name"/>.
    /// </summary>
    public IReadOnlyDictionary<string, BranchName> ByName => _byName;

    /// <summary>
    /// Finds a branch by its name.
    /// </summary>
    /// <param name="name">The branch name.</param>
    /// <returns>The branch or null.</returns>
    public BranchName? Find( string name ) => ByName.GetValueOrDefault( name ); 

}
