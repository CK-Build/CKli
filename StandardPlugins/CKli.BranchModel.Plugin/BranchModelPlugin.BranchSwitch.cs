using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Handles the branch model defined for a World.
/// </summary>
public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Implements "ckli branch switch" (the command is handled by the HotZone plugin, that provides the
    /// <paramref name="versionResolver"/>): switches the working folder to the specified branch of the branch model.
    /// <para>
    /// Without <paramref name="create"/>, the branch (its "dev/" branch when it exists) or its closest existing branch
    /// is checked out. With it, the branch is opened in each repository exactly like "ckli branch open" opens an already
    /// opened branch (see <see cref="HotBranch.OpenOrPredict"/>): created if needed, synchronized, and its "dev/" branch
    /// is checked out. The branch model is never changed: the branch must be in it.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The minimal CKli context.</param>
    /// <param name="branch">The branch name to switch to.</param>
    /// <param name="create">True to open the branch in the repositories where it doesn't exist and synchronize it.</param>
    /// <param name="all">Consider all the Repos of the current World.</param>
    /// <param name="versionResolver">
    /// Optional provider of the resolver of the package versions that conflict when <paramref name="create"/> is true
    /// (see <see cref="HotBranch.Synchronize"/>). It receives the branch to switch to.
    /// </param>
    /// <returns>True on success, false otherwise.</returns>
    public bool SwitchBranch( IActivityMonitor monitor,
                              CKliEnv context,
                              string branch,
                              bool create,
                              bool all,
                              Func<IActivityMonitor, BranchName, IPackageVersionResolver?>? versionResolver )
    {
        if( !GetReposAndBranch( monitor, context, all, branch, out var repos, out var branchName, out var isDevName ) )
        {
            return false;
        }
        var infos = GetInfos( monitor, repos );
        if( infos == null ) return false;
        if( create )
        {
            Func<IActivityMonitor, IPackageVersionResolver?>? resolver = versionResolver != null
                                                                            ? m => versionResolver( m, branchName )
                                                                            : null;
            var report = new BranchMergeReport( dryRun: false, prepareMerges: resolver != null );
            bool opened = OpenOrPredict( monitor, infos, branchName, resolver, report, $"switching to '{branchName}'" );
            report.Display( context.Screen );
            return opened;
        }
        bool success = true;
        foreach( var info in infos )
        {
            var b = info.Branches[branchName.Index];
            var target = b.Exists ? b : info.GetRequiredClosestExistingBranch( monitor, branchName );
            if( target == null )
            {
                success = false;
                continue;
            }
            Throw.DebugAssert( target.Exists );
            // When the user specified a "dev/", we ensure that the "dev/" branch exists.
            if( isDevName && target == b )
            {
                target.EnsureDevBranch();
            }
            // We always switch to the "dev/" branch if it exists even if it has not been specified.
            success &= target.Repo.GitRepository.Checkout( monitor, target.GitDevBranch ?? target.GitBranch );
        }
        return success;
    }
}
