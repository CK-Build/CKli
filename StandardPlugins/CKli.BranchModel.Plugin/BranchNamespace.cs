using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Captures the opened branches that makes the hot zone.
/// </summary>
public sealed partial class BranchNamespace : IEquatable<BranchNamespace>
{
    static readonly string[] _autoPrevRootBranchNames = ["stable", "main", "master", "root", "trunk", "mother", "primary", "develop"];

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
    /// <param name="ltsName">Optional <see cref="WorldName.LTSName"/>.</param>
    /// <param name="sMainLine">
    /// Main branches line starts with the root branch name followed by the
    /// opened <see cref="BranchLinkType"/><see cref="CSVersionKindExtensions.ToBranchName(CSVersionKind)"/>.
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
                            List<(string BranchName, CSVersionKind Kind, BranchLinkType Link)> mainLine,
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
            int ltsPrefixLength = 0;
            if( ltsName != null )
            {
                name = ltsName + '/' + name;
                ltsPrefixLength = ltsName.Length + 1;
            }

            var b = new BranchName( ltsPrefixLength, BranchLinkType.None, name, 0, CSVersionKind.Stable, null );
            result.Add( b );
            byName.Add( b.Name, b );
            while( e.MoveNext() )
            {
                name = e.Current.BranchName;
                if( ltsName != null ) name = ltsName + '/' + name;
                b = new BranchName( ltsPrefixLength, e.Current.Link, name, b.Index + 1, e.Current.Kind, b );
                result.Add( b );
                byName.Add( b.Name, b );
            }
            mainLineCount = result.Count;
            AddExploBranches( ltsPrefixLength, exploratories, ltsName, result, byName, parent: null );
            branches = result.DrainToImmutable();
        }

        static List<(string BranchName, CSVersionKind Kind, BranchLinkType Link)> ParseMainLine( string? configuration )
        {
            var result = new List<(string BranchName, CSVersionKind Kind, BranchLinkType Link)>();
            ReadOnlySpan<char> h = configuration;
            if( !h.SkipWhiteSpaces() || h.Length == 0 )
            {
                result.Add( (_defaultRootName, CSVersionKind.Stable, BranchLinkType.None) );
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
            if( CSVersionKindExtensions.TryParse( name, out var kind, StringComparison.Ordinal ) && kind != CSVersionKind.Stable )
            {
                throw new CKException( $"""
                    Invalid root branch name in BranchModel MainLine configuration: '{name}' must not be one of the prerelease name nor 'explo'.
                    It is typically 'stable' or 'main'.
                    """ );
            }
            result.Add( (new string( name ), CSVersionKind.Stable, BranchLinkType.None) );

            CSVersionKind prevKind = CSVersionKind.Stable;
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
                CSVersionKind csKind = CSVersionKind.None;
                if( !CSVersionKindExtensions.TryMatch( ref h, ref csKind, StringComparison.Ordinal ) )
                {
                    throw new CKException( $"""
                        Invalid BranchModel MainLine configuration.
                        Expected lowercase Conformant SVersion prerelease name ('alpha', 'bravo',... 'zulu'), got:
                        {h}
                        """ );
                }
                if( prevKind <= csKind )
                {
                    throw new CKException( $"""
                        Invalid prelease ordering in BranchModel MainLine configuration: '{prevKind.ToBranchName()}' must appear before '{csKind.ToBranchName()}'.
                        """ );
                }
                prevKind = csKind;
                result.Add( (csKind.ToBranchName(), csKind, linkType) );
            }
            return result;

        }

        _ltsName = ltsName;
    }

    static void AddExploBranches( int ltsPrefixLength,
                                  IEnumerable<XElement> exploratories,
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
            // LinkType is optional (defaults to CI).
            BranchLinkType linkType = BranchLinkType.CI;
            var sLink = (string?)e.Attribute( XNames.Link );
            if( !string.IsNullOrWhiteSpace( sLink ) )
            {
                linkType = BranchLinkTypeExtensions.ParseLinkType( sLink );
            }
            var hName = name.AsSpan();
            var h = hName;
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
            if( SVersion.IsReservedExploratoryName( bName ) )
            {
                throw new CKException( $"""
                            Invalid exploratory branch Name attribute in BranchModel configuration: '{hName}'.
                            {ReservedExploratoryNameError}
                            """ );
            }
            if( !hasExplo || (ltsName != null && !hasLTSName) )
            {
                name = new string( hName );
                if( !hasExplo ) name = "explo/" + name;
                if( ltsName != null )
                {
                    name = ltsName + '/' + name;
                }
            }

            if( byName.ContainsKey( name ) )
            {
                Throw.CKException( $"""
                            Duplicate branch name '{name}' in BranchModel configuration:
                            {e}
                            """ );
            }
            var b = new BranchName( ltsPrefixLength, linkType, name, result.Count, CSVersionKind.Exploratory, parent );
            result.Add( b );
            byName.Add( b.Name, b );
            // Recursive call with explicit parent.
            AddExploBranches( ltsPrefixLength, e.Elements( XNames.Explo ), ltsName, result, byName, parent: b );
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
    /// The error tail shared by every site that refuses a <see cref="SVersion.IsReservedExploratoryName"/>
    /// exploratory name: one that starts with "ci-" or ends with "-ci".
    /// <para>
    /// A "-ci" suffix qualifies a branch name to distinguish its CI builds from its regular versions -
    /// this is what the Publish plugin's "Published/index.json" does - so an exploratory name must not be
    /// able to spell one: "explo/spike-ci" and the CI line of "explo/spike" would be the same name. Only
    /// the exploratory names need this: "alpha" to "zulu" are fixed and, by design, none of them collides.
    /// </para>
    /// <para>
    /// The rule itself belongs to <see cref="SVersion"/>, which applies it to the version side
    /// (SetExploratoryName and the parser). Only the wording is ours.
    /// </para>
    /// </summary>
    internal const string ReservedExploratoryNameError = """An exploratory branch name must not start with "ci-" nor end with "-ci".""";

    /// <summary>
    /// Gets the "stable" root branch name.
    /// </summary>
    public BranchName Root => _root;

    /// <summary>
    /// Gets the main line branches that start the <see cref="Branches"/> (from 'zulu' to 'alpha').
    /// </summary>
    public ReadOnlySpan<BranchName> MainLineBranches => _branches.AsSpan().Slice( 0, _mainLineCount );

    /// <summary>
    /// Gets the exploratory branches that follow the <see cref="MainLineBranches"/>.
    /// </summary>
    public ReadOnlySpan<BranchName> ExploratoryBranches => _branches.AsSpan().Slice( _mainLineCount );

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
    /// <para>
    /// This transparently prepends the <see cref="WorldName.LTSName"/> if it's missing.
    /// </para>
    /// </summary>
    /// <param name="name">The branch name.</param>
    /// <returns>The branch or null.</returns>
    public BranchName? Find( string name ) => _byName.GetValueOrDefault( WorldName.EnsureLTSPrefix( _ltsName, name ) );

    /// <summary>
    /// Finds the <paramref name="branchName"/> in this <see cref="BranchNamespace"/> or emits an error
    /// if this is not an existing branch name.
    /// <para>
    /// This transparently prepends the <see cref="WorldName.LTSName"/> if it's missing.
    /// </para>
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
    /// Finds the <paramref name="branchName"/> in this <see cref="BranchNamespace"/> or throws an <see cref="InvalidOperationException"/>.
    /// <para>
    /// This transparently prepends the <see cref="WorldName.LTSName"/> if it's missing.
    /// </para>
    /// </summary>
    /// <param name="branchName">The branch name to lookup.</param>
    /// <returns>The name.</returns>
    public BranchName FindRequired( string branchName )
    {
        var b = Find( branchName );
        if( b == null ) Throw.InvalidOperationException( $"Branch '{branchName}' doesn't exist." );
        return b;
    }

    /// <summary>
    /// Finds a <see cref="BranchName"/> that corresponds to a Conformant Semantic Version (see <see cref="CSVersionKind"/>).
    /// <para>
    /// See also <see cref="BranchName.Match(SVersion)"/>.
    /// </para>
    /// </summary>
    /// <param name="version">The version for which the associated branch should be found.</param>
    /// <returns>The branch name or null if not found.</returns>
    public BranchName? Find( SVersion version ) => _branches.FirstOrDefault( b => b.Match( version ) );


    /// <summary>
    /// Finds a <see cref="BranchName"/> that corresponds to a Conformant Semantic Version (see <see cref="CSVersionKind"/>)
    /// or returns null and logs an error if not found.
    /// <para>
    /// See also <see cref="BranchName.Match(SVersion)"/>.
    /// </para>
    /// </summary>
    /// <param name="version">The version for which the associated branch should be found.</param>
    /// <returns>The branch name or null if not found.</returns>
    public BranchName? FindRequired( IActivityMonitor monitor, SVersion version )
    {
        var b = Find( version );
        if( b == null )
        {
            monitor.Error( $"""
                Unable to find a branch for version '{version}'. Defined branches are:
                {_branches.Select( b => b.Name ).Concatenate()}
                """ );
        }
        return b;
    }

    /// <summary>
    /// Gets the branches that correspond to the <see cref="Root"/> and <see cref="CSVersionKind"/> prereleases as a string.
    /// </summary>
    /// <returns>The mainline.</returns>
    public string GetMainLine() => _mainLineCount == 1 ? _root.Name : GetMainLineBuilder().ToString();

    StringBuilder GetMainLineBuilder()
    {
        var sb = new StringBuilder( _root.Name );
        for( int i = 1; i < _mainLineCount; i++ )
        {
            var b = _branches[i];
            sb.Append( ' ' ).Append( b.LinkType.ToCodeString() ).Append( ' ' ).Append( _branches[i].Name );
        }
        return sb;
    }

    /// <summary>
    /// Gets the &lt;Explo ... &gt; elements if any.
    /// </summary>
    /// <returns>The elements for exploratory branches.</returns>
    public IEnumerable<XElement> GetExplo()
    {
        int nbExplo = _branches.Length - _mainLineCount;
        if( nbExplo == 0 ) return [];

        var exploNodes = new XElement[nbExplo];
        for( int i = 0; i < nbExplo; i++ )
        {
            var b = _branches[_mainLineCount + i];
            var parent = b.Parent;
            Throw.DebugAssert( parent != null );
            XElement e;
            int iParent = parent.Index - _mainLineCount;
            if( iParent >= 0 )
            {
                e = ToXml( b.Name, b.LinkType, null );
                exploNodes[iParent].Add( e );
            }
            else
            {
                e = ToXml( b.Name, b.LinkType, parent.Name );
            }
            exploNodes[i] = e;
        }
        return exploNodes.Where( e => e.Attribute( XNames.Parent ) != null );
    }

    static XElement ToXml( string name, BranchLinkType type, string? parentName )
    {
        return new XElement( XNames.Explo,
                                new XAttribute( XNames.Name, name ),
                                type != BranchLinkType.CI
                                    ? new XAttribute( XNames.Link, type.ToString() )
                                    : null,
                                parentName != null
                                    ? new XAttribute( XNames.Parent, parentName )
                                    : null );
    }

    /// <summary>
    /// Gets whether all <see cref="BranchName.Equals(BranchName?)"/> are equal between both <see cref="Branches"/>.
    /// </summary>
    /// <param name="other">The other namespace.</param>
    /// <returns><c>true</c> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <c>false</c>.</returns>
    public bool Equals( BranchNamespace? other ) => other != null && _branches.SequenceEqual( other._branches );

    /// <summary>
    /// See <see cref="Equals(BranchNamespace?)"/>.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns><c>true</c> if the specified object is equal to the current object; otherwise, <c>false</c>.</returns>
    public override bool Equals( object? obj ) => Equals( obj as BranchNamespace );

    /// <summary>
    /// See <see cref="Equals(BranchNamespace?)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach( var b in _branches )
            {
                hash = hash * 31 + b.GetHashCode();
            }
            return hash;
        }
    }

    /// <summary>
    /// Gets the branches displayed in a tree.
    /// </summary>
    /// <returns>The branches.</returns>
    public string GetDisplayTree()
    {
        var sb = new StringBuilder( _root.Name );
        AddChildren( sb, 2, _root, _branches );
        return sb.ToString();

        static void AddChildren( StringBuilder sb, int depth, BranchName parent, ImmutableArray<BranchName> branches )
        {
            foreach( var b in branches.AsSpan().Slice( parent.Index + 1 ) )
            {
                if( b.Parent == parent )
                {
                    sb.Append( ' ', depth ).Append( b.LinkType.ToCodeString() ).Append( ' ' ).Append( b.Name );
                    AddChildren( sb, depth + 2, b, branches );
                }
            }
        }
    }


    /// <summary>
    /// Gets the <see cref="GetMainLine()"/> string followed by the <see cref="GetExplo()"/> elements.
    /// </summary>
    /// <returns>The main line and exploratory branches.</returns>
    public override string ToString()
    {
        if( _mainLineCount == _branches.Length ) return GetMainLine();
        var sb = GetMainLineBuilder();
        foreach( var e in GetExplo() )
        {
            sb.AppendLine().Append( e.ToString() );
        }
        return sb.ToString();
    }


    internal string GetNoPreviousRootBranchFoundMessage()
    {
        return $"""
            No '{_autoPrevRootBranchNames.Concatenate( "', '" )}' branch found.
            The '{_root.Name}' should be created manually.
            """;
    }

    internal Branch? GetPreviousRootBranch( IActivityMonitor monitor, GitRepository repository )
    {
        return _autoPrevRootBranchNames.Where( n => !n.Equals( Root.Name, StringComparison.OrdinalIgnoreCase ) )
                                       .Select( n => repository.GetBranch( monitor, n, CK.Core.LogLevel.Info ) )
                                       .FirstOrDefault( b => b != null );
    }

}
