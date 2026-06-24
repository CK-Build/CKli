using CK.Core;
using CKli.Core;
using System;
using System.ComponentModel.DataAnnotations;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    [Description( """
        Opens a Conformant SVersion branch it it doesn't exist already.
        - For prerelease branches ('alpha', 'bravo', 'charlie', ...'zulu'), the parent branch is based on the lexicographic order.
        - For exploratory branches ('explo/name'), the parent branch is the currently checked out branch. 
        """ )]
    [CommandPath( "branch open" )]
    public bool BranchOpen( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Branch name to open." )]
                            string branchName )
    {
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null ) return false;

        var branchInfo = GetWithoutIssue( monitor, repo, "opening a branch" );
        if( branchInfo == null ) return false;

        BranchName? parent = null;
        var h = branchName.AsSpan();
        if( h.TryMatch( "explo/" )
            && BranchNamespace.MatchBranchSegment( ref h, out _ )
            && h.SkipWhiteSpaces()
            && h.Length == 0 )
        {
            parent = GetValidBranchName( monitor, repo.GitStatus.CurrentBranchName );
            if( parent == null ) return false;
        }
        else if( CSVersionKindExtensions.TryParse( h, out var csKind, StringComparison.Ordinal ) )
        {
            var n = _namespace.Find( csKind.ToPrerelease() );
            if( n != null )
            {
                monitor.Error( $"""
                    Branch name '{branchName}' is already opened.
                    """ );
                return false;
            }
            parent = branchInfo.GetClosestExistingBranch( n );
        }
        else
        {
            monitor.Error( $"""
                Branch name '{branchName}' must be a Conformant SVersion prerelease name ('alpha', 'bravo', ...'zulu') or an exploratory 'explo/name'.
                """ );
            return false;
        }

        return true;
    }

}

