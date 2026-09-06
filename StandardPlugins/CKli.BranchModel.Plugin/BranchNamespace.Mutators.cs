using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchNamespace
{
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

        var branchName = prerelease.ToBranchName();
        if( _ltsName != null ) branchName = _ltsName + '/' + branchName;

        return Rebuild( _ltsName,
                        _root,
                        _branches.Skip( 1 ).Take( _mainLineCount - 1 )
                                 .Where( b => b.Name != branchName )
                                 .Select( b => (b.LinkType, b.VersionKind, b.Name) )
                                 .Append( (linkType, prerelease, branchName) )
                                 .OrderByDescending( e => e.Item2 ),
                        _branches.Skip( _mainLineCount ).Select( b => (b.LinkType, b.Name, b.Parent!.Name) ),
                        branchName );
    }

    /// <summary>
    /// Creates a new namespace with a new or updated "explo/" branch name.
    /// </summary>
    /// <param name="branchName">The "explo/name" branch name to add.</param>
    /// <param name="linkType">The optional link type. Defaults to <see cref="BranchLinkType.CI"/> for a new branch.</param>
    /// <param name="parent">
    /// The optional parent branch.
    /// <list type="bullet">
    ///     <item>When adding (the branch is new), this defaults to the most instable opened prerelease branch.</item>
    ///     <item>When updating an existing branch, the current parent is unchanged when this is not specified.</item>
    /// </list>
    /// </param>
    /// <returns>The namespace and its updated branch name.</returns>
    public (BranchNamespace, BranchName) AddOrUpdateExplo( string branchName, BranchLinkType linkType = BranchLinkType.None, BranchName? parent = null )
    {
        Throw.CheckArgument( branchName.StartsWith( "explo/", StringComparison.Ordinal ) );
        Throw.CheckArgument( "BranchName mismatch.", parent == null || Branches[parent.Index] == parent );

        var segmentName = branchName.AsSpan( 6 );
        if( !ValidBranchSegment().IsMatch( segmentName ) )
        {
            Throw.ArgumentException( nameof( branchName ), $"Invalid branch name '{branchName}': segment '{segmentName}' must a lowercase identifier with optional '-' and '_' characters." );
        }
        if( IsReservedExploratoryName( segmentName ) )
        {
            Throw.ArgumentException( nameof( branchName ), $"Invalid branch name '{branchName}'. {ReservedExploratoryNameError}" );
        }

        if( _ltsName != null ) branchName = _ltsName + '/' + branchName;
        if( _byName.TryGetValue( branchName, out var exists )
            && (linkType is BranchLinkType.None || exists.LinkType == linkType)
            && (parent is null || exists.Parent == parent) )
        {
            return (this, exists);
        }
        if( exists != null )
        {
            return Rebuild( _ltsName,
                            _root,
                            _branches.Skip( 1 ).Take( _mainLineCount - 1 )
                                     .Select( b => (b.LinkType, b.VersionKind, b.Name) ),
                            _branches.Skip( _mainLineCount )
                                     .Where( b => b.Name != branchName )
                                     .Select( b => (b.LinkType, b.Name, b.Parent!.Name) )
                                     .Append( (linkType is BranchLinkType.None ? exists.LinkType : linkType, branchName, (parent ?? exists.Parent)!.Name) ),
                            branchName );

        }
        return Rebuild( _ltsName,
                        _root,
                        _branches.Skip( 1 ).Take( _mainLineCount - 1 )
                                 .Select( b => (b.LinkType, b.VersionKind, b.Name) ),
                        _branches.Skip( _mainLineCount )
                                 .Select( b => (b.LinkType, b.Name, b.Parent!.Name) )
                                 .Append( (linkType is BranchLinkType.None ? BranchLinkType.CI : linkType, branchName, (parent ?? _branches[_mainLineCount - 1]).Name) ),
                        branchName );
    }

    /// <summary>
    /// Returns a new namespace with the specified branch name removed.
    /// </summary>
    /// <param name="branchName">The branch name to remove. Cannot be the <see cref="Root"/>.</param>
    /// <returns>A new namespace.</returns>
    public BranchNamespace Remove( BranchName branchName )
    {
        Throw.CheckArgument( "Root branch cannot be removed.", !branchName.IsRoot );
        Throw.CheckArgument( "BranchName mismatch.", Branches[branchName.Index] == branchName );

        return Rebuild( _ltsName,
                        _root,
                        _branches.Skip( 1 ).Take( _mainLineCount - 1 ).Where( b => b != branchName ).Select( b => (b.LinkType, b.VersionKind, b.Name) ),
                        _branches.Skip( _mainLineCount )
                                 .Where( b => b != branchName )
                                 .Select( b => (b.LinkType, b.Name, ((b.Parent == branchName ? b.Parent.Parent : b.Parent) ?? _root).Name) ) );
    }

    static (BranchNamespace, BranchName) Rebuild( string? ltsName,
                                                 BranchName root,
                                                 IEnumerable<(BranchLinkType T, CSVersionKind K, string N)> mainLine,
                                                 IEnumerable<(BranchLinkType T, string N, string P)> exploratories,
                                                 string returnedBranchName )
    {
        var ns = Rebuild( ltsName, root, mainLine, exploratories );
        return (ns, ns._byName[returnedBranchName]);
    }


    static BranchNamespace Rebuild( string? ltsName,
                                    BranchName root,
                                    IEnumerable<(BranchLinkType T, CSVersionKind K, string N)> mainLine,
                                    IEnumerable<(BranchLinkType T, string N, string P)> exploratories )
    {
        var byName = new Dictionary<string, BranchName>() { { root.Name, root } };
        var branches = ImmutableArray.CreateBuilder<BranchName>();
        branches.Add( root );
        var previous = root;
        int mainLineCount = 1;
        foreach( var (type, kind, name) in mainLine )
        {
            Throw.DebugAssert( ltsName == null
                               || name.StartsWith( ltsName )
                                  && name.Length > ltsName.Length + 1
                                  && name[ltsName.Length] == '/'
                                  && CSVersionKindExtensions.TryParse( name.AsSpan( root._ltsPrefixLength ), out _, StringComparison.Ordinal ) );
            var newM = new BranchName( root._ltsPrefixLength, type, name, branches.Count, kind, previous );
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
            return roots.DescendantsAndSelf( XNames.Explo ).FirstOrDefault( e => (string?)e.Attribute( XNames.Name ) == name );
        }

        // Populates the roots and collects sub explo branches (if any).
        List<(BranchLinkType T, string N, string P)>? sub = null;
        foreach( var (type, name, parentName) in exploratories )
        {
            var hParent = parentName.AsSpan();
            bool hasLTSName = ltsName != null && hParent.TryMatch( ltsName, StringComparison.Ordinal ) && hParent.TryMatch( '/' );
            if( hParent.TryMatch( "explo/", StringComparison.Ordinal ) )
            {
                sub ??= new List<(BranchLinkType T, string N, string P)>();
                sub.Add( (type, name, parentName) );
            }
            else
            {
                // Find the most instable parent from "parentName".
                // => Throw if not found.
                var parent = root.Name == parentName
                                ? root
                                : branches.Last( b => b.Name.CompareTo( parentName, StringComparison.Ordinal ) >= 0 );
                roots.Add( ToXml( name, type, parent.Name ) );
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
                        parent.Add( ToXml( candidate.N, candidate.T, null ) );
                        sub.RemoveAt( sub.Count - 1 );
                        atLeastOne = true;
                    }
                }
                // Avoid forever loop if something's wrong in the code. This should NEVER happen.
                Throw.CheckState( "Internal error: exploratory branch parent not found.", atLeastOne );
            }
            while( sub.Count > 0 );
        }

        AddExploBranches( root._ltsPrefixLength, roots, ltsName, branches, byName, null );

        return new BranchNamespace( ltsName, branches.DrainToImmutable(), mainLineCount, byName );
    }


}
