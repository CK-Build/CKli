using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Linq;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Implements "ckli branch sync" (the command is handled by the HotZone plugin, that provides the
    /// <paramref name="versionResolver"/>): synchronizes the specified branch with its closest parent branch.
    /// <para>
    /// A missing root branch fails the command before anything is done. Only the branch and its closest existing parent
    /// matter: a repository where one of them has an issue is skipped and fails the command; the other ones are still
    /// synchronized. When the branch (or its "dev/") was checked out, its "dev/" branch is checked out afterwards.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branch">The branch name to synchronize.</param>
    /// <param name="link">Optional link type (Regular, CI or Full) to synchronize with instead of the configured one.</param>
    /// <param name="all">Consider all the Repos of the current World (even if current path is in a Repo).</param>
    /// <param name="versionResolver">
    /// Optional provider of the resolver of the package versions that conflict (see <see cref="HotBranch.Synchronize"/>).
    /// It receives the branch to synchronize.
    /// </param>
    /// <param name="dryRun">
    /// True to only display what the synchronization would do (see <see cref="HotBranch.PredictSynchronize"/>): no merge is
    /// done. This returns what the synchronization would return: false when a merge would be left in progress or when
    /// it would fail.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public bool SynchronizeBranch( IActivityMonitor monitor,
                                   CKliEnv context,
                                   string branch,
                                   string? link,
                                   bool all,
                                   Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? versionResolver,
                                   bool dryRun = false )
    {
        if( !ParseLink( monitor, link, allowManual: false, out var linkType ) )
        {
            return false;
        }
        if( !GetReposAndBranch( monitor, context, all, branch, out var repos, out var branchName, out var isDevName ) )
        {
            return false;
        }
        Throw.DebugAssert( (branchName.LinkType is BranchLinkType.None) == branchName.IsRoot );
        if( branchName.IsRoot && linkType != BranchLinkType.None )
        {
            monitor.Error( $"The root branch '{branchName}' has no parent: the --link option cannot be used." );
            return false;
        }
        // Read before the BranchModelInfo exist: obtaining one can auto fix a useless "dev/" branch, which
        // deletes it and checks out its base branch.
        var checkedOut = repos.Select( r => r.GitRepository.CurrentBranchName ).ToArray();
        var infos = GetInfos( monitor, repos );
        if( infos == null ) return false;
        Func<IActivityMonitor, IPackageVersionResolver?>? resolver = versionResolver != null
                                                                        ? m => versionResolver( m, branchName )
                                                                        : null;
        bool success = true;
        var report = new BranchMergeReport( dryRun, prepareMerges: resolver != null );
        for( int i = 0; i < infos.Length; i++ )
        {
            var b = infos[i].Branches[branchName.Index];
            // A branch that doesn't exist has nothing to synchronize, unless its orphan "dev/" branch carries work:
            // this is an issue.
            if( !b.Exists && !b.HasOrphanDevBranch )
            {
                continue;
            }
            // A skipped repository is a failure: the branch has not been synchronized there.
            if( !b.CheckNoIssue( monitor, $"synchronizing '{branchName}'" )
                || !b.SynchronizeOrPredict( monitor, linkType, resolver, report ) )
            {
                success = false;
                continue;
            }
            if( dryRun ) continue;
            Throw.DebugAssert( "An orphan \"dev/\" branch is an issue.", b.Exists );
            // When the branch was checked out, its work goes on in its "dev/" branch if it exists (a merge may
            // have just created it): the next commit must not land on the base branch.
            if( checkedOut[i] == b.GitBranch.FriendlyName || checkedOut[i] == b.GitDevBranch?.FriendlyName )
            {
                success &= b.Repo.GitRepository.Checkout( monitor, b.GitDevBranch ?? b.GitBranch );
            }
        }
        report.Display( context.Screen );
        return success;
    }
}

