using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Collections.Generic;

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
    /// <param name="branchName">The branch name to switch to.</param>
    /// <param name="forceOpen">True to open an unexisting branch.</param>
    /// <param name="all">Consider all the Repos of the current World.</param>
    /// <returns>True on success, false otherwise.</returns>
    [Description( "Switch the working folder to the specified branch, optionally opening it." )]
    [CommandPath( "branch switch" )]
    public bool BranchSwitch( IActivityMonitor monitor,
                              CKliEnv context,
                              [Description( "Branch name to checkout." )]
                              string branchName,
                              [Description( "Open the branch if it doesn't exist, instead of switching to the closest opened one." )]
                              [OptionName("--force-open,-o")]
                              bool forceOpen = false,
                              [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                              bool all = false )
    {
        var repos = all
                    ? World.GetAllDefinedRepo( monitor )
                    : World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( repos == null ) return false;

        bool isDevName = branchName.StartsWith( "dev/" );
        if( isDevName ) branchName = branchName.Substring( 4 );
        var name = _namespace.FindRequired( monitor, branchName );
        if( name == null ) return false;

        if( !GetClosestActiveBranches( monitor, repos, name, out var closestActive ) )
        {
            return false;
        }

        bool success = true;
        foreach( var b in closestActive )
        {
            Throw.DebugAssert( b.Exists );
            bool exists = b.BranchName == name && (!isDevName || b.GitDevBranch != null);
            if( !exists && forceOpen )
            {
                var info = b.BranchModelInfo;
                var git = info.Repo.GitRepository.Repository;
                Branch bHot = b.GitBranch;
                if( b.BranchName != name )
                {
                    // The branch provided by the user is not active. We create the hot branch
                    // from the closest one.
                    Throw.DebugAssert( """
                        Nothing could have fetched or create the branch since  the HotBranch has been created
                        (GetBranch has been called - an existing remote would have created the local).
                        """, info.Repo.GitRepository.GetBranch( monitor, name.Name, CK.Core.LogLevel.None ) == null );
                    // Since the branch doesn't exist, we create it from its closest active branch regardless
                    // of any configured link type (the branch must start somewhere).
                    bHot = BranchLink.CreateAheadBranch( info.Repo.GitRepository, b.GitBranch.Tip, name.Name, withEmptyInitializationCommit: false );
                }
                // Branch bHot is okay.
                // GetClosestActiveBranches checked that no orphan "dev/" exists for the branch.
                Throw.DebugAssert( git.Branches[name.DevName] == null );
                if( isDevName )
                {
                    // We must create the "dev/" branch.
                    bHot = BranchLink.CreateAheadBranch( info.Repo.GitRepository, bHot.Tip, name.DevName, withEmptyInitializationCommit: false );
                }
                success &= b.Repo.GitRepository.Checkout( monitor, bHot );
            }
            else
            {
                // Should this be:
                //      isDevName ? (b.GitDevBranch ?? b.GitBranch) : b.GitBranch
                // Currently we always switch to the "dev/" branch if it exists even if it has
                // not been specified.
                // This introduces an asymmetry with the create case above...
                // ...but this seems "natural".
                success &= b.Repo.GitRepository.Checkout( monitor, b.GitDevBranch ?? b.GitBranch );
            }
        }
        return success;
    }

    bool GetClosestActiveBranches( IActivityMonitor monitor, IReadOnlyList<Repo> repos, BranchName name, out HotBranch[] closestActive )
    {
        closestActive = new HotBranch[repos.Count];
        bool success = true;
        int idx = 0;
        foreach( var repo in repos )
        {
            var info = Get( monitor, repo );
            var b = info.GetClosestExistingBranch( name );
            if( b == null )
            {
                monitor.Error( $"Missing root '{_namespace.Root.Name}' branch in '{repo.DisplayPath}'. Please create it or use 'ckli issue' to fix this." );
                success = false;
            }
            else
            {
                // Stops on this issue because it will introduces an ambiguity.
                if( b.BranchName != name && info.Branches[name.Index].HasOrphanDevBranch )
                {
                    monitor.Error( $"""
                        Branch '{name.DevName}' in '{repo.DisplayPath}' exists but its base '{name.Name}' branch doesn't exist.
                        Please remove '{name.DevName}' branch or use 'ckli issue' to fix this.
                        """ );
                    success = false;
                }
                closestActive[idx] = b;
            }
            ++idx;
        }
        return success;
    }
}

