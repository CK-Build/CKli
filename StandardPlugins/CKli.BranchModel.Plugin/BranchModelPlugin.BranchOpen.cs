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

        var normalizedBranchName = ParseAndNormalizeBranchName( monitor, branchName, out var csPrerelease );
        if( normalizedBranchName == null ) return false;

        // The system state is... what it is.
        // We can have a git branch and/or a BranchName: we must not rely here on any kind of synchronization
        // between these 2 aspects.
        var existingBranchName = _namespace.Find( normalizedBranchName );
        if( existingBranchName != null )
        {
            var hotBranch = branchInfo.GetClosestExistingBranch( existingBranchName );
            Throw.DebugAssert( "The root branch necessarily exists (because there's no issue).", hotBranch != null );
            if( !hotBranch.Exists )
            {
                // The git branch doesn't exist: we must create it.
            }
        }

        var existingGitBranch = repo.GitRepository.GetBranch( monitor, normalizedBranchName, LogLevel.None );
        // For explo branch, the checked out branch must be an opened one.
        var parent = _namespace.FindRequired( monitor, repo.GitStatus.CurrentBranchName );
        if( parent == null ) return false;

        return true;
    }

    string? ParseAndNormalizeBranchName( IActivityMonitor monitor, string branchName, out CSVersionKind csPrerelease )
    {
        var h = branchName.AsSpan();
        if( h.TryMatch( "explo/", StringComparison.Ordinal ) )
        {
            csPrerelease = CSVersionKind.None;
            if( !BranchNamespace.MatchBranchSegment( ref h, out _ ) || !h.SkipWhiteSpaces() || h.Length != 0 )
            {
                monitor.Error( $"Invalid '{branchName}'. The branch name must be a lowercase ASCII identifier (which may contain dash '-' or underscore '_')" );
                return null;
            }
        }
        else if( CSVersionKindExtensions.TryParse( h, out csPrerelease, StringComparison.Ordinal ) )
        {
            branchName = World.Name.EnsureLTSPrefix( branchName );
        }
        else
        {
            monitor.Error( $"""
                Invalid branch name '{branchName}'. It must be a Conformant SVersion prerelease name ('alpha', 'bravo', ...'zulu') or an exploratory 'explo/name' branch name.
                """ );
            return null;
        }
        return World.Name.EnsureLTSPrefix( branchName );
    }
}

