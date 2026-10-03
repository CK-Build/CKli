using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

sealed partial class BranchIssueBuilder
{
    /// <summary>
    /// Useless "dev/" branches (merged into their base) are deleted. This is implicit when the BranchModel's
    /// AutoFixDevBranch is true: the other commands delete them silently.
    /// </summary>
    sealed class RemovableBranchesIssue : World.Issue
    {
        readonly List<(Branch Branch, Branch Base)> _removables;

        RemovableBranchesIssue( IRenderable body, List<(Branch Branch, Branch Base)> removables, Repo repo, bool implicitIssue )
            : base( "Removable branches.", body, repo, implicitIssue )
        {
            _removables = removables;
        }

        public static RemovableBranchesIssue Create( ScreenType screenType, Repo repo, List<(Branch Branch, Branch Base)> removables, bool autoFixDevBranch )
        {
            var names = removables.Select( r => $"- '{r.Branch.FriendlyName}' is merged into '{r.Base.FriendlyName}'." )
                                  .Concatenate( Environment.NewLine );
            var body = screenType.Text( $"""
                                        {names}
                                        {(removables.Count > 1 ? "They" : "It")} can be deleted.
                                        """ );
            return new RemovableBranchesIssue( body, removables, repo, autoFixDevBranch );
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world, CancellationToken cancellation )
        {
            Throw.DebugAssert( Repo != null );
            var git = Repo.GitRepository;
            bool success = true;
            foreach( var (branch, branchBase) in _removables )
            {
                if( branch.IsCurrentRepositoryHead )
                {
                    monitor.Info( $"Branch to remove is the current head. Switching to its base branch '{branchBase.FriendlyName}'." );
                    if( !git.Checkout( monitor, branchBase ) )
                    {
                        success = false;
                        continue;
                    }
                }
                success &= git.DeleteBranch( monitor, branch, DeleteGitBranchMode.WithTrackedBranch );
            }
            return ValueTask.FromResult( success );
        }
    }

    /// <summary>
    /// The missing base of a "dev/" branch is recreated where the "dev/" branch left its closest existing parent
    /// (see <see cref="HotBranch.RestoreMissingBase"/>). This is implicit when the BranchModel's AutoFixDevBranch is
    /// true: the other commands recreate it silently.
    /// </summary>
    sealed class MissingBaseBranchesIssue : World.Issue
    {
        readonly List<HotBranch> _branches;

        MissingBaseBranchesIssue( IRenderable body, List<HotBranch> branches, Repo repo, bool implicitIssue )
            : base( "Missing base branches.", body, repo, implicitIssue )
        {
            _branches = branches;
        }

        public static MissingBaseBranchesIssue Create( ScreenType screenType, Repo repo, List<HotBranch> branches, bool autoFixDevBranch )
        {
            var names = branches.Select( b => $"- '{b.GitDevBranch!.FriendlyName}' lacks its base '{b.BranchName.Name}' branch." )
                                .Concatenate( Environment.NewLine );
            var body = screenType.Text( $"""
                                        {names}
                                        {(branches.Count > 1 ? "They" : "It")} can be recreated where the "dev/" branch left its parent.
                                        """ );
            return new MissingBaseBranchesIssue( body, branches, repo, autoFixDevBranch );
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world, CancellationToken cancellation )
        {
            bool success = true;
            foreach( var b in _branches )
            {
                success &= b.RestoreMissingBase( monitor );
            }
            return ValueTask.FromResult( success );
        }
    }
}
