using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Handles the branch model defined for a World.
/// </summary>
public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Switch the working folder to the specified branch.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The minimal CKli context.</param>
    /// <param name="branch">The branch name to switch to.</param>
    /// <param name="create">True to open an unexisting branch.</param>
    /// <param name="all">Consider all the Repos of the current World.</param>
    /// <returns>True on success, false otherwise.</returns>
    [Description( "Switch the working folder to the specified branch, optionally creating it in the Repo." )]
    [CommandPath( "branch switch" )]
    public bool BranchSwitch( IActivityMonitor monitor,
                              CKliEnv context,
                              [Description( "Branch name to checkout." )]
                              string branch,
                              [Description( "Create and synchronize the branch if it doesn't exist, instead of switching to the closest existing one." )]
                              [OptionName("--create,-c")]
                              bool create = false,
                              [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                              bool all = false )
    {
        if( !GetReposAndBranch( monitor, context, all, branch, out var repos, out var branchName, out var isDevName ) )
        {
            return false;
        }
        bool success = true;
        foreach( var repo in repos )
        {
            var info = Get( monitor, repo );
            var b = info.Branches[branchName.Index];
            if( !create || b.EnsureExists( monitor ) )
            {
                var target = b.Exists ? b : info.GetRequiredClosestExistingBranch( monitor, branchName );
                if( target != null )
                {
                    Throw.DebugAssert( target.Exists );
                    // If create is true, we will synchronize but the "dev/" branch may not be created
                    // (if the BranchLinkType is manual or if there's nothing to synchronize) so we
                    // ensure that the "dev/" branch exists.
                    if( isDevName && target == b && create )
                    {
                        target.EnsureDevBranch();
                    }
                    // When -c is used, we Synchronize the branch with its remote and parent: this unifies
                    // the behavior regardless of the initial branch existence.
                    Throw.DebugAssert( "create => we are on the target branch.", !create || target == b );
                    if( create && !target.Synchronize( monitor, _commitProvider ) )
                    {
                        success = false;
                    }
                    else
                    {
                        // Currently we always switch to the "dev/" branch if it exists even if it has
                        // not been specified.
                        success &= target.Repo.GitRepository.Checkout( monitor, target.GitDevBranch ?? target.GitBranch );
                    }
                }
                else
                {
                    success = false;
                }
            }
            else
            {
                success = false;
            }
        }
        return success;
    }
}

