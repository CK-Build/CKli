using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Linq;
using System.Numerics;
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
    /// <para>
    /// This constructor is public mainly for tests.
    /// </para>
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
            AddExploBranches( exploratories, ltsName, result, byName, parent: null );
            branches = result.DrainToImmutable();
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
                if( !h.TryMatchLinkTypeCode( out linkType ) )
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

    static void AddExploBranches( IEnumerable<XElement> exploratories,
                                  string? ltsName,
                                  ImmutableArray<BranchName>.Builder result,
                                  Dictionary<string, BranchName> byName,
                                  BranchName? parent )
    {
        foreach( var e in exploratories )
        {
            // The Parent name is required or rejected.
            var parentAttr = e.Attribute( XNames.Parent );
            if( parent == null )
            {
                var pName = (string?)parentAttr;
                parent = string.IsNullOrWhiteSpace( pName ) ? null : byName.GetValueOrDefault( pName );
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
            h.TryMatchLinkTypeCode( out var linkType );
            if( linkType == BranchLinkType.None ) linkType = BranchLinkType.CI;
            var hName = h;
            bool hasLTSName = ltsName != null && h.TryMatch( ltsName, StringComparison.Ordinal ) && h.TryMatch( '/' );
            bool hasExplo = h.TryMatch( "explo/", StringComparison.Ordinal );
            if( !MatchBranchSegment( ref h, out var bName ) || h.Length > 0 )
            {
                throw new CKException( $"""
                            Invalid exploratory branch Name attribute in BranchModel configuration.
                            Expected a lowercase ASCII identifier (which may contain dash '-' or underscore '_'), got:
                            {hName}
                            """ );
            }
            if( !hasExplo || (ltsName != null && !hasLTSName) )
            {
                name = new string( hName );
                if( !hasExplo ) name = "explo/" + name;
                if( ltsName != null ) name = ltsName + '/' + name;
            }

            if( byName.ContainsKey( name ) )
            {
                Throw.CKException( $"""
                            Duplicate branch name '{name}' in BranchModel configuration:
                            {e}
                            """ );
            }
            var b = new BranchName( linkType, name, result.Count, parent );
            result.Add( b );
            byName.Add( b.Name, b );
            AddExploBranches( e.Elements( XNames.Explo ), ltsName, result, byName, b );
        }
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
    public BranchName? Find( string name ) => _byName.GetValueOrDefault( WorldName.EnsureLTSPrefix( _ltsName, name ) );

    /// <summary>
    /// Finds the <paramref name="branchName"/> in this <see cref="BranchNamespace"/> or emits an error
    /// if this is not an existing branch name.
    /// </summary>
    /// <param name="monitor">The monitor to emit the error.</param>
    /// <param name="branchName">The branch name to lookup.</param>
    /// <returns>The name or null on error.</returns>
    public BranchName? FindRequired( IActivityMonitor monitor, string branchName )
    {
        var b = Find( branchName );
        if( b == null )
        {
            monitor.Error( $"""
                Invalid opened branch '{WorldName.EnsureLTSPrefix( _ltsName, branchName )}'.
                Opened branches are '{_branches.Select( b => b.Name ).Concatenate( "', '" )}'.
                """ );
        }
        return b;
    }

    /// <summary>
    /// Creates a new namespace with a new or updated branch name.
    /// </summary>
    /// <param name="linkType">The link type.</param>
    /// <param name="prerelease">
    /// The prerelease to consider.
    /// Must be between <see cref="CSVersionKind.Alpha"/> and <see cref="CSVersionKind.Zulu"/>.
    /// </param>
    /// <returns>The namespace and its updated branch name.</returns>
    public (BranchNamespace, BranchName) AddOrUpdate( BranchLinkType linkType, CSVersionKind prerelease )
    {
        Throw.CheckArgument( prerelease is >= CSVersionKind.Alpha and <= CSVersionKind.Zulu );

        var branchName = prerelease.ToPrerelease();
        if( _ltsName != null ) branchName = _ltsName + '/' + branchName;

        return Rebuild( _ltsName,
                        _root,
                        _branches.Skip( 1 ).Take( _mainLineCount - 1 )
                                 .Where( b => b.Name != branchName )
                                 .Select( b => (b.LinkType, b.Name) )
                                 .Append( (linkType, branchName) )
                                 .OrderByDescending( e => e.Item2 ),
                        _branches.Skip( _mainLineCount ).Select( b => (b.LinkType, b.Name, b.Parent!.Name) ),
                        branchName );
    }

    /// <summary>
    /// Creates a new namespace with a new or updated "explo/" branch name.
    /// </summary>
    /// <param name="branchName">The "explo/name" branch name to add.</param>
    /// <param name="linkType">The optional link type Defaults to <see cref="BranchLinkType.CI"/> for a new branch.</param>
    /// <param name="parent">The optional parent branch. Defaults to the most instable opened prerelease branch for a new branch.</param>
    /// <returns>The namespace and its updated branch name.</returns>
    public (BranchNamespace, BranchName) AddOrUpdateExplo( string branchName, BranchLinkType? linkType = null, BranchName? parent = null )
    {
        Throw.CheckArgument( branchName.StartsWith( "explo/", StringComparison.Ordinal ) && ValidBranchSegment().IsMatch( branchName, 6 ) );
        Throw.CheckArgument( "BranchName mismatch.", parent == null || Branches[parent.Index] == parent );

        if( _ltsName != null ) branchName = _ltsName + '/' + branchName;
        if( _byName.TryGetValue( branchName, out var exists )
            && (linkType is null || exists.LinkType == linkType.Value)
            && (parent is null || exists.Parent == parent) )
        {
            return (this, exists);
        }
        if( exists != null )
        {
            return Rebuild( _ltsName,
                            _root,
                            _branches.Skip( 1 ).Take( _mainLineCount - 1 )
                                     .Select( b => (b.LinkType, b.Name) ),
                            _branches.Skip( _mainLineCount )
                                     .Where( b => b.Name != branchName )
                                     .Select( b => (b.LinkType, b.Name, b.Parent!.Name) )
                                     .Append( (linkType ?? exists.LinkType, branchName, (parent ?? exists.Parent)!.Name) ),
                            branchName );

        }
        return Rebuild( _ltsName,
                        _root,
                        _branches.Skip( 1 ).Take( _mainLineCount - 1 )
                                 .Select( b => (b.LinkType, b.Name) ),
                        _branches.Skip( _mainLineCount )
                                 .Select( b => (b.LinkType, b.Name, b.Parent!.Name) )
                                 .Append( (linkType ?? BranchLinkType.CI, branchName, (parent ?? _branches[_mainLineCount-1]).Name) ),
                        branchName );
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

        return Rebuild( _ltsName,
                        _root,
                        _branches.Skip( 1 ).Take( _mainLineCount - 1 ).Where( b => b != branchName ).Select( b => (b.LinkType, b.Name)),
                        _branches.Skip( _mainLineCount ).Where( b => b != branchName ).Select( b => (b.LinkType, b.Name, b.Parent!.Name) ) );
    }

    static (BranchNamespace,BranchName) Rebuild( string? ltsName,
                                                 BranchName root,
                                                 IEnumerable<(BranchLinkType T, string N)> mainLine,
                                                 IEnumerable<(BranchLinkType T, string N, string P)> exploratories,
                                                 string returnedBranchName )
    {
        var ns = Rebuild( ltsName, root, mainLine, exploratories );
        return (ns, ns._byName[returnedBranchName]);
    }


    static BranchNamespace Rebuild( string? ltsName,
                                    BranchName root,
                                    IEnumerable<(BranchLinkType T, string N)> mainLine,
                                    IEnumerable<(BranchLinkType T, string N, string P)> exploratories )
    {
        var byName = new Dictionary<string, BranchName>() { { root.Name, root } };
        var branches = ImmutableArray.CreateBuilder<BranchName>();
        branches.Add( root );
        var previous = root;
        int mainLineCount = 1;
        foreach( var (type, name) in mainLine )
        {
            Throw.DebugAssert( ltsName == null
                               || name.StartsWith( ltsName )
                                  && name.Length > ltsName.Length + 1
                                  && name[ltsName.Length] == '/'
                                  && CSVersionKindExtensions.TryParse( name.AsSpan( ltsName.Length + 1 ), out _, StringComparison.Ordinal ) );
            var newM = new BranchName( type, name, branches.Count, previous );
            previous = newM;
            branches.Add( newM );
            byName.Add( newM.Name, newM );
            ++mainLineCount;
        }

        // To handle the exploratories, instead of reinventing an intermediate tree structure, we use
        // the XElement structure and reuse the AddExploBranches method.
        //
        // This is not really efficient but be really don't care.
        // First, there's few chances to use sub explo branches.
        // Second, this orders/groups the exploratory branches under their parents: the configuration rewriting is optimized,
        // the read will be efficient and this is what matters, not the mutation of the branch model.
        //
        List<XElement> roots = new List<XElement>();

        static XElement? Find( List<XElement> roots, string name )
        {
            return roots.DescendantsAndSelf( XNames.Explo ).FirstOrDefault( e => (string?)e.Attribute(XNames.Name) == name );
        }

        // Populates the roots and collects sub explo branches (if any).
        List<(BranchLinkType T, string N, string P)>? sub = null;
        foreach( var (type,name,parentName) in exploratories )
        {
            var h = name.AsSpan();
            bool hasLTSName = ltsName != null && h.TryMatch( ltsName, StringComparison.Ordinal ) && h.TryMatch( '/' );
            if( h.TryMatch( "explo/", StringComparison.Ordinal ) )
            {
                sub ??= new List<(BranchLinkType T, string N, string P)>();
                sub.Add( (type, name, parentName) );
            }
            else
            {
                var parent = root.Name == parentName
                                ? root
                                : branches.First( b => b.Name.CompareTo( parentName, StringComparison.Ordinal ) <= 0 );
                roots.Add( new XElement( XNames.Explo, new XAttribute( XNames.Name, $"{type.ToCodeString()} {name}" ),
                                                        new XElement( XNames.Parent, parent.Name ) ) );
            }
        }
        if( sub != null )
        {
            // Process the sub explo branches until all of them are anchored.
            do
            {
                bool atLeastOne = false;
                for( int i = sub.Count - 1; i >= 0; i-- )
                {
                    var candidate = sub[i];
                    var parent = Find( roots, candidate.P );
                    if( parent != null )
                    {
                        parent.Add( new XElement( XNames.Explo, new XAttribute( XNames.Name, $"{candidate.T.ToCodeString()} {candidate.N}" ) ) );
                        sub.RemoveAt( sub.Count - 1 );
                        atLeastOne = true;
                    }
                }
                // Avoid forever loop if something's wrong in the code. This should NEVER happen.
                Throw.CheckState( "Internal error: exploratory branch parent not found.", atLeastOne );
            }
            while( sub.Count > 0 );
        }

        AddExploBranches( roots, ltsName, branches, byName, null );

        return new BranchNamespace( ltsName, branches.DrainToImmutable(), mainLineCount, byName );
    }
}
