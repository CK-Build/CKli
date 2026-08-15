using CK.Core;
using CKli.Core;
using System;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{
    /// <summary>
    /// Synchronize the specified branch with its closest parent branch.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branch">The branch name to synchronize.</param>
    /// <param name="mode">Specifies the mode (Release, CI or Full). Overrides the configured mode.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Synchronize the specified branch with its closest parent branch.
        """ )]
    [CommandPath( "branch sync" )]
    public bool BranchSync( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Branch name to synchronize." )]
                            string branch,
                            [Description( "Specifies the mode (Release, CI or Full). Overrides the configured link type." )]
                            string? mode = null,
                            [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                            bool all = false )
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

        bool success = true;
        int hasIssueCount = 0;
        foreach( var repo in repos )
        {
            var info = GetWithoutIssue( monitor, repo );
            if( info == null )
            {
                ++hasIssueCount;
            }
            else
            {
                var b = info.Branches[branchName.Index];
                if( b.Exists )
                {
                    success &= b.Synchronize( monitor, linkType );
                }
            }
        }
        if( hasIssueCount != 0 )
        {
            monitor.Warn( $"One or more repositories have issues. They have been skipped." );
        }
        return success;
    }
}

