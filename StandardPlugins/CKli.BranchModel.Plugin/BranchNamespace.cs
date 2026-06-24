using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Captures the opened branches that makes the hot zone.
/// </summary>
public sealed partial class BranchNamespace
{
    const string _defaultRootName = "stable";

    readonly BranchName _root;
    readonly ImmutableArray<BranchName> _branches;
    readonly Dictionary<string, BranchName> _byName;
    readonly string? _ltsName;
    readonly int _mainLineCount;

    /// <summary>
    /// Initializes a new namespace.
    /// </summary>
    /// <param name="ltsName">Optional <see cref="CKli.Core.WorldName.LTSName"/>.</param>
    /// <param name="sMainLine">
    /// Main branches line starts with the root branch name followed by the
    /// opened <see cref="BranchLinkType"/><see cref="CSVersionKindExtensions.ToPrerelease(CSVersionKind)"/>.
    /// </param>
    /// <param name="exploratories">Opened exploratory branches.</param>
    public BranchNamespace( string? ltsName,
                            string? sMainLine,
                            IEnumerable<XElement> exploratories )
    {
        Create( ltsName,
                ParseMainLine( sMainLine ),
                exploratories,
                out _branches,
                out _mainLineCount,
                out _byName );
        _root = _branches[0];
        _ltsName = ltsName;

        static void Create( string? ltsName,
                            List<(string BranchName, BranchLinkType Link)> mainLine,
                            IEnumerable<XElement> exploratories,
                            out ImmutableArray<BranchName> branches,
                            out int mainLineCount,
                            out Dictionary<string, BranchName> byName )
        {
            Throw.DebugAssert( mainLine.Count > 0 );
            var result = ImmutableArray.CreateBuilder<BranchName>();
            byName = new Dictionary<string, BranchName>();
            var e = mainLine.GetEnumerator();
            e.MoveNext();
            // root branch.
            var name = e.Current.BranchName;
            if( ltsName != null ) name = ltsName + '/' + name;

            var b = new BranchName( BranchLinkType.None, name, 0, null );
            result.Add( b );
            byName.Add( b.Name, b );
            while( e.MoveNext() )
            {
                name = e.Current.BranchName;
                if( ltsName != null ) name = ltsName + '/' + name;
                b = new BranchName( e.Current.Link, name, b.Index + 1, b );
                result.Add( b );
                byName.Add( b.Name, b );
            }
            mainLineCount = result.Count;
            AddBranches( exploratories, ltsName, result, byName, parent: null );
            branches = result.DrainToImmutable();


            static void AddBranches( IEnumerable<XElement> exploratories,
                                     string? ltsName,
                                     ImmutableArray<BranchName>.Builder result,
                                     Dictionary<string, BranchName> index,
                                     BranchName? parent )
            {
                foreach( var e in exploratories )
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
                    if( ltsName != null ) name = ltsName + '/' + name;

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

        _ltsName = ltsName;
    }

    BranchNamespace( string? ltsName,
                     ImmutableArray<BranchName> branches,
                     int mainLineCount,
                     Dictionary<string, BranchName> byName )
    {
        _ltsName = ltsName;
        _branches = branches;
        _root = branches[0];
        _mainLineCount = mainLineCount;
        _byName = byName;
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

    internal static bool MatchBranchSegment( ref ReadOnlySpan<char> h, out ReadOnlySpan<char> branchName )
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
    public BranchName? Find( string name ) => _byName.GetValueOrDefault( EnsureLTSPrefix( name ) );

    string EnsureLTSPrefix( string name )
    {
        if( _ltsName != null
            && (!name.StartsWith( _ltsName ) || name.Length <= _ltsName.Length + 1 || name[_ltsName.Length] != '/') )
        {
            name = $"{_ltsName}/{name}";
        }
        return name;
    }

    /// <summary>
    /// Finds the <paramref name="branchName"/> in this <see cref="BranchNamespace"/> or emits an error
    /// if this is not an existing branch name.
    /// </summary>
    /// <param name="monitor">The monitor to emit the error.</param>
    /// <param name="branchName">The branch name to lookup.</param>
    /// <returns>The name or null on error.</returns>
    public BranchName? Find( IActivityMonitor monitor, string branchName )
    {
        var b = Find( branchName );
        if( b == null )
        {
            monitor.Error( $"""
                Invalid opened branch '{EnsureLTSPrefix( branchName )}'.
                Opened branches are '{_branches.Select( b => b.Name ).Concatenate( "', '" )}'.
                """ );
        }
        return b;
    }

    /// <summary>
    /// Creates a new namespace with a new or updated branch name.
    /// </summary>
    /// <param name="linkType">The link type.</param>
    /// <param name="prerelease">The prerelease to consider. Must be between <see cref="CSVersionKind.Alpha"/> and <see cref="CSVersionKind.Zulu"/>.</param>
    /// <returns>A new namespace.</returns>
    public BranchNamespace AddOrUpdate( BranchLinkType linkType, CSVersionKind prerelease )
    {
        Throw.CheckArgument( prerelease is >= CSVersionKind.Alpha and <= CSVersionKind.Zulu );

        var newByName = new Dictionary<string, BranchName>( _byName );
        var branchName = prerelease.ToPrerelease();
        if( _ltsName != null ) branchName = _ltsName + '/' + branchName;

        BranchName? newB = null;
        ImmutableArray<BranchName>.Builder? newBranches = null;
        int iNewIndex = 0;
        for( int i = 1; i < _mainLineCount; i++ )
        {
            var b = _branches[i];
            int cmp = b.Name.CompareTo( branchName, StringComparison.Ordinal );
            if( cmp == 0 )
            {
                if( linkType == b.LinkType ) return this;
                newB = new BranchName( linkType, branchName, b.Index, b.Parent );
                newByName[branchName] = newB;
                return new BranchNamespace( _ltsName, _branches.SetItem( i, newB ), _mainLineCount, newByName );
            }
            if( cmp > 0 )
            {
                iNewIndex = i;
                newB = new BranchName( linkType, branchName, i, _branches[i - 1] );
                newBranches = ImmutableArray.CreateBuilder<BranchName>( _branches.Length + 1 );
                newBranches.AddRange( _branches, i );
                newBranches.Add( newB );
                newByName.Add( branchName, newB );
                while( i < _mainLineCount )
                {
                    var e = _branches[i];
                    var newE = new BranchName( e.LinkType, e.Name, i + 1, newB );
                    newBranches.Add( newE );
                    newByName[e.Name] = newE;
                    newB = newE;
                    ++i;
                }
                break;
            }
        }
        Throw.DebugAssert( newB != null && newBranches != null );
        for( int i = _mainLineCount; i < _branches.Length; i++ )
        {
            var e = _branches[i];
            Throw.DebugAssert( e.Name.StartsWith( "explo/" ) && e.Parent != null );
            var parent = e.Parent.Index < iNewIndex ? e.Parent : newBranches[e.Parent.Index + 1];
            var newE = new BranchName( e.LinkType, e.Name, i + 1, parent );
            newBranches.Add( newE );
            newByName[e.Name] = newE;
        }
        return new BranchNamespace( _ltsName, newBranches.MoveToImmutable(), _mainLineCount + 1, newByName );
    }

    /// <summary>
    /// Creates a new namespace with a new or updated "explo/" branch name.
    /// </summary>
    /// <param name="branchName">The "explo/name" branch name to add.</param>
    /// <param name="linkType">The optional link type Defaults to <see cref="BranchLinkType.CI"/> for a new branch.</param>
    /// <param name="parent">The optional parent branch. Defaults to </param>
    /// <returns>A new namespace.</returns>
    public BranchNamespace AddOrUpdateExplo( string branchName, BranchLinkType? linkType = null, BranchName? parent = null )
    {
        Throw.CheckArgument( branchName.StartsWith( "explo/", StringComparison.Ordinal ) && ValidBranchSegment().IsMatch( branchName, 6 ) );
        Throw.CheckArgument( "BranchName mismatch.", parent == null || Branches[parent.Index] == parent );

        if( _ltsName != null ) branchName = _ltsName + '/' + branchName;
        if( _byName.TryGetValue( branchName, out var exists )
            && exists.LinkType == linkType
            && exists.Parent == parent )
        {
            return this;
        }
        if( exists != null )
        {
            Throw.DebugAssert( exists.Parent != null );
            BranchName newE;
            if( parent == exists.Parent )
            {
                newE = new BranchName( linkType ?? exists.LinkType, branchName, exists.Index, parent );
                var newByName = new Dictionary<string, BranchName>( _byName );
                newByName[branchName] = newE;
                return new BranchNamespace( _ltsName, _branches.SetItem( exists.Index, newE ), _mainLineCount, newByName );
            }
            return Remove( exists ).AddExplo( branchName, linkType ?? exists.LinkType, parent ?? exists.Parent );
        }
        return AddExplo( branchName, linkType ?? BranchLinkType.CI, parent ?? _root );
    }

    BranchNamespace AddExplo( string branchName, BranchLinkType linkType, BranchName parent )
    {
        BranchName newB;
        if( parent.Index < _mainLineCount )
        {
            // The parent is a Conformant SVersion prerelease.
            newB = new BranchName( linkType, branchName, _branches.Length, parent );
            var newByName = new Dictionary<string, BranchName>( _byName ) { { branchName, newB } };
            return new BranchNamespace( _ltsName, _branches.Add( newB ), _mainLineCount, newByName );
        }
        // The parent is another "explo/".
        var newBranches = ImmutableArray.CreateBuilder<BranchName>( _branches.Length + 1 );
        newBranches.AddRange( _branches, parent.Index + 1 );
        newB = new BranchName( linkType, branchName, newBranches.Count, parent );
        newBranches.Add( newB );
        for( int i = parent.Index + 1; i < _branches.Length; i++ )
        {
            var b = _branches[i];
            Throw.DebugAssert( "We are not on the root.", b.Parent != null );
            int parentIndex = b.Parent.Index;
            if( parentIndex >= newB.Index )
            {
                ++parentIndex;
            }
            newBranches.Add( new BranchName( b.LinkType, b.Name, b.Index + 1, newBranches[parentIndex] ) );
        }
        var branches = newBranches.MoveToImmutable();
        return new BranchNamespace( _ltsName, branches, _mainLineCount, branches.ToDictionary( b => b.Name ) );
    }

    /// <summary>
    /// Returns a new namespace with the specified branch name removed.
    /// </summary>
    /// <param name="branchName">The branch name to remove. Cannot be the <see cref="Root"/>.</param>
    /// <returns>A new namespace.</returns>
    public BranchNamespace Remove( BranchName branchName )
    {
        Throw.CheckArgument( "Root branch cannot be removed.", branchName.Parent != null );
        Throw.CheckArgument( "BranchName mismatch.", Branches[branchName.Index] == branchName );
        var newBranches = ImmutableArray.CreateBuilder<BranchName>( _branches.Length - 1 );
        newBranches.AddRange( _branches, branchName.Index );
        for( int i = branchName.Index + 1; i < _branches.Length; i++ )
        {
            var b = _branches[i];
            Throw.DebugAssert( "We are not on the root.", b.Parent != null );
            int parentIndex = b.Parent.Index;
            if( parentIndex > branchName.Index )
            {
                --parentIndex;
                Throw.DebugAssert( parentIndex >= 0 );
            }
            else if( parentIndex == branchName.Index )
            {
                parentIndex = b.Parent.Parent?.Index ?? 0;
            }
            var newB = new BranchName( b.LinkType, b.Name, b.Index - 1, newBranches[parentIndex] );
            newBranches.Add( newB );
        }
        int mainLineCount = _mainLineCount < branchName.Index ? _mainLineCount - 1 : _mainLineCount;
        var branches = newBranches.MoveToImmutable();
        return new BranchNamespace( _ltsName, branches, mainLineCount, branches.ToDictionary( b => b.Name ) );
    }

}
