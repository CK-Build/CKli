using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Implements "ckli branch sync" (the command is handled by the HotZone plugin, that provides the
    /// <paramref name="versionResolver"/>): synchronizes the specified branch with its closest parent branch.
    /// <para>
    /// A repository with issues is skipped and fails the command; the other ones are still synchronized. When the branch
    /// (or its "dev/") was checked out, its "dev/" branch is checked out afterwards.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branch">The branch name to synchronize.</param>
    /// <param name="mode">Specifies the mode (Release, CI or Full). Overrides the configured mode.</param>
    /// <param name="all">Consider all the Repos of the current World (even if current path is in a Repo).</param>
    /// <param name="versionResolver">
    /// Optional provider of the resolver of the package versions that conflict (see <see cref="HotBranch.Synchronize"/>).
    /// It receives the branch to synchronize.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public bool SynchronizeBranch( IActivityMonitor monitor,
                                   CKliEnv context,
                                   string branch,
                                   string? mode,
                                   bool all,
                                   Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? versionResolver )
    {
        if( !ParseLink( monitor, mode, allowManual: false, out var linkType ) )
        {
            return false;
        }
        if( !GetReposAndBranch( monitor, context, all, branch, out var repos, out var branchName, out var isDevName ) )
        {
            return false;
        }
        Throw.DebugAssert( (branchName.LinkType is BranchLinkType.None) == branchName.IsRoot );
        Func<IActivityMonitor, IPackageVersionResolver?>? resolver = versionResolver != null
                                                                        ? m => versionResolver( m, branchName )
                                                                        : null;
        bool success = true;
        foreach( var repo in repos )
        {
            // Read before the BranchModelInfo exists: obtaining it can auto fix a useless "dev/" branch, which
            // deletes it and checks out its base branch.
            var checkedOut = repo.GitRepository.CurrentBranchName;
            var info = GetWithoutIssue( monitor, repo, before: null );
            if( info == null )
            {
                // A skipped repository is a failure: the branch has not been synchronized there.
                monitor.Error( $"Repository '{repo.DisplayPath}' has issues: branch '{branchName}' has not been synchronized. Please fix them first." );
                success = false;
                continue;
            }
            var b = info.Branches[branchName.Index];
            if( !b.Exists )
            {
                continue;
            }
            if( !b.Synchronize( monitor, linkType, resolver ) )
            {
                success = false;
                continue;
            }
            // When the branch was checked out, its work goes on in its "dev/" branch if it exists (a merge may
            // have just created it): the next commit must not land on the base branch.
            if( checkedOut == b.GitBranch.FriendlyName || checkedOut == b.GitDevBranch?.FriendlyName )
            {
                success &= repo.GitRepository.Checkout( monitor, b.GitDevBranch ?? b.GitBranch );
            }
        }
        return success;
    }
}

