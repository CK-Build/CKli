using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Captures the branches that makes the hot zone.
/// </summary>
public sealed partial class BranchNamespace
{
    const string _defaultRootName = "stable";

    readonly BranchName _root;
    readonly ImmutableArray<BranchName> _branches;
    readonly Dictionary<string, BranchName> _byName;

    internal BranchNamespace( string? ltsName,
                              string? sMainLine,
                              XElement? exploratory )
    {
        Create( ltsName,
                ParseMainLine( sMainLine ),
                exploratory,
                out _branches,
                out _byName );
        _root = _branches[0];

        static void Create( string? ltsName,
                            List<(string BranchName, BranchLinkType Link)> mainLine,
                            XElement? exploratory,
                            out ImmutableArray<BranchName> branches,
                            out Dictionary<string, BranchName> byName )
        {
            Throw.DebugAssert( mainLine.Count > 0 );
            var result = ImmutableArray.CreateBuilder<BranchName>();
            byName = new Dictionary<string, BranchName>();
            var e = mainLine.GetEnumerator();
            e.MoveNext();
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
            if( exploratory != null )
            {
                AddBranches( exploratory.Elements( XNames.Explo ), ltsName, result, byName, parent: null );
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
                    var parentAttr = e.Attribute( XNames.Parent );
                    if( parent == null )
                    {
                        var pName = (string?)parentAttr;
                        parent = string.IsNullOrWhiteSpace( pName ) ? null : index.GetValueOrDefault( pName );
                        if( parent == null )
                        {
                            throw new CKException( $"""Unable to find Parent="{pName}" parent branch in BranchModel configuration.""" );
                        }
                    }
                    else if( parentAttr != null )
                    {
                        throw new CKException( $"""Unexpected Parent="..." attribute in BranchModel configuration (parent is '{parent}' branch).""" );
                    }
                    var name = (string?)e.Attribute( XNames.Name );
                    if( string.IsNullOrWhiteSpace( name ) )
                    {
                        throw new CKException( $"""
                            Expected Name="..." attribute in BranchModel configuration.
                            """ );
                    }
                    // Handling Name="<linkType>name".
                    // LinkType is optional (defaults to CI).
                    var h = name.AsSpan();
                    MatchLinkType( ref h, out var linkType );
                    var hName = h;
                    bool hasExplo = h.TryMatch( "explo/", StringComparison.Ordinal );
                    if( !MatchBranchSegment( ref h, out var bName ) || h.Length > 0 )
                    {
                        throw new CKException( $"""
                            Invalid exploratory branch Name attribute in BranchModel configuration.
                            Expected a lowercase ASCII identifier (which may contain dash '-' or underscore '_'), got:
                            {hName}
                            """ );
                    }
                    name = new string( hName );
                    if( !hasExplo ) name = "explo/" + name;
                    name = ltsName == null
                                ? name
                                : ltsName + '/' + name;
                    if( index.ContainsKey( name ) )
                    {
                        Throw.CKException( $"""
                            Duplicate branch name '{name}' in BranchModel configuration:
                            {e}
                            """ );
                    }
                    var b = new BranchName( linkType, name, result.Count, parent );
                    result.Add( b );
                    index.Add( b.Name, b ); 
                }
            }
        }

        static List<(string BranchName, BranchLinkType Link)> ParseMainLine( string? configuration )
        {
            var result = new List<(string BranchName, BranchLinkType Link)>();
            ReadOnlySpan<char> h = configuration;
            if( !h.SkipWhiteSpaces() || h.Length == 0 )
            {
                result.Add( (_defaultRootName, BranchLinkType.None) );
                return result;
            }
            if( !MatchBranchSegment( ref h, out var name ) )
            {
                throw new CKException( $"""
                    Invalid root branch name in BranchModel MainLine configuration.
                    Expected a lowercase ASCII identifier (which may contain dash '-' or underscore '_'), got:
                    {h}
                    """ );
            }
            if( CSVersionKindExtensions.TryParse( name, out _, StringComparison.Ordinal ) )
            {
                throw new CKException( $"""
                    Invalid root branch name in BranchModel MainLine configuration: '{name}' must not be one of the prerelease name.
                    It is typically 'stable' or 'main'.
                    """ );
            }
            result.Add( (new string( name ), BranchLinkType.None) );

            CSVersionKind prevKind = CSVersionKind.None;
            while( h.SkipWhiteSpaces() && h.Length > 0 )
            {
                BranchLinkType linkType = BranchLinkType.CI;
                if( !MatchLinkType( ref h, out linkType ) )
                {
                    throw new CKException( $"""
                        Unable to parse link type in BranchModel MainLine configuration.
                        Expected '||' (None), '|' (Release), '->' (CI) or '=>' (Full), got:
                        {h}
                        """ );
                }
                h.SkipWhiteSpaces();
                if( !CSVersionKindExtensions.TryParse( h, out var csKind, StringComparison.Ordinal ) )
                {
                    throw new CKException( $"""
                        Invalid BranchModel MainLine configuration.
                        Expected lowercase Conformant SVersion prerelease name ('alpha', 'bravo',... 'zulu'), got:
                        {h}
                        """ );
                }
                if( prevKind >= csKind )
                {
                    throw new CKException( $"""
                        Invalid prelease ordering in BranchModel MainLine configuration: '{prevKind.ToPrerelease()}' must appear before '{csKind.ToPrerelease()}'.
                        """ );
                }
                result.Add( (csKind.ToPrerelease(), linkType) );
            }
            return result;

        }
    }

    static bool MatchLinkType( ref ReadOnlySpan<char> h, out BranchLinkType t )
    {
        t = BranchLinkType.CI;
        if( h.TryMatch( '|' ) )
        {
            t = h.TryMatch( '|' )
                    ? BranchLinkType.None
                    : BranchLinkType.Release;
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

    static bool MatchBranchSegment( ref ReadOnlySpan<char> h, out ReadOnlySpan<char> branchName )
    {
        var e = ValidBranchSegment().EnumerateMatches( h );
        if( e.MoveNext() )
        {
            Throw.DebugAssert( e.Current.Index == 0 );
            branchName = h.Slice( 0, e.Current.Length );
            h = h.Slice( branchName.Length );
            return true;
        }
        branchName = default;
        return false;
    }

    [GeneratedRegex( "^[a-z][0-9a-z_-]+", RegexOptions.CultureInvariant )]
    private static partial Regex ValidBranchSegment();

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
