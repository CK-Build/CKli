using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;

namespace CKli.HotZone.Plugin;

// "ckli branch sync" and "ckli branch list" are implemented by the BranchModel plugin: they are handled here because
// the package versions that conflict when merging a branch's link are resolved like a build of the branch would
// update them, and that needs the branch's HotGraph.
public sealed partial class HotZonePlugin
{
    /// <summary>
    /// Synchronizes the specified branch with its closest parent branch.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branch">The branch name to synchronize.</param>
    /// <param name="mode">Specifies the mode (Release, CI or Full). Overrides the configured mode.</param>
    /// <param name="all">Consider all the Repos of the current World (even if current path is in a Repo).</param>
    /// <param name="failOnConflict">True to fail without touching anything when a merge conflicts beyond the package versions.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Synchronize the specified branch with its closest parent branch.
        The package versions that conflict are resolved the way a build of the branch updates them. When other conflicts
        remain, the merge is left in progress in the working folder (the "dev/" branch is checked out): resolve them and
        commit the merge.
        """ )]
    [CommandPath( "branch sync" )]
    public bool BranchSync( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Branch name to synchronize." )]
                            string branch,
                            [Description( "Specifies the mode (Release, CI or Full). Overrides the configured link type." )]
                            string? mode = null,
                            [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                            bool all = false,
                            [Description( "Fails without touching anything when a merge conflicts beyond the package versions." )]
                            bool failOnConflict = false )
    {
        return _branchModel.SynchronizeBranch( monitor, context, branch, mode, all, CreateVersionResolverProvider(), failOnConflict );
    }

    /// <summary>
    /// Closes a Conformant SVersion branch in the current repositories by integrating it in the "dev/" branch of its
    /// closest parent.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branchName">The branch name to close.</param>
    /// <param name="discard">True to keep the branch where it is and not integrate it in its closest parent.</param>
    /// <param name="failOnConflict">True to fail without touching anything when a merge conflicts beyond the package versions.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Closes a Conformant SVersion branch in the current repositories by integrating it in the "dev/" branch of its
        closest parent: the parent's base branch receives it when its "dev/" branch is integrated.
        The upstreams whose versions of this branch are consumed are also closed. The branch is removed from the
        branch model when no repository of the World has it anymore.
        The package versions that conflict are resolved the way a build of the parent updates them. When other conflicts
        remain, the merge is left in progress in the working folder: resolve them, commit the merge and close the branch
        again.
        """ )]
    [CommandPath( "branch close" )]
    public bool BranchClose( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description( "Branch name to close." )]
                             string branchName,
                             [Description( "Only remove the branch from the branch model (must be run at the root of the World): the Git branches are left as-is." )]
                             bool discard = false,
                             [Description( "Fails without touching anything when a merge conflicts beyond the package versions." )]
                             bool failOnConflict = false )
    {
        return _branchModel.CloseBranch( monitor, context, branchName, discard, CreateVersionResolverProvider(), failOnConflict );
    }

    /// <summary>
    /// Displays the opened branches of the World and, for each of them, what "ckli branch sync" would do and the
    /// repositories where it has changes.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <returns>True on success, false if a solution cannot be read.</returns>
    [Description( """
        Displays the opened branches of the World and how each one is linked to its parent.
        For each branch, the repositories where it has changes are summarized by their tips (the ones that have no
        upstream repository where the branch has changes), the number of repositories where the branch is opened
        without changes and the weight of the branch: the number of repositories and projects that a build of the
        branch touches (the ones with changes and all their downstreams).
        When a branch is behind its link, a "Branch sync" column tells what "ckli branch sync" would do: the number of
        fast-forwards and merges, and the repositories where the merge conflicts.
        """ )]
    [CommandPath( "branch list" )]
    public bool BranchList( IActivityMonitor monitor, CKliEnv context )
    {
        return _branchModel.DisplayBranchList( monitor, context, CreateVersionResolverProvider() );
    }

    /// <summary>
    /// Creates a provider of <see cref="IPackageVersionResolver"/> that computes the HotGraph of a branch the first
    /// time its resolver is needed (only a merge that conflicts needs it) and caches it, failure included.
    /// The merges go to the "dev/" branches: the graph is the CI one, as for a CI build.
    /// </summary>
    Func<IActivityMonitor, BranchName, IPackageVersionResolver?> CreateVersionResolverProvider()
    {
        var cache = new Dictionary<BranchName, IPackageVersionResolver?>();
        return ( monitor, branch ) =>
        {
            if( !cache.TryGetValue( branch, out var resolver ) )
            {
                using( monitor.OpenInfo( $"Computing the Hot Graph of branch '{branch}' to resolve the package versions that conflict." ) )
                {
                    var graph = GetHotGraph( monitor, branch, isCIBuild: true, [] );
                    resolver = graph?.GetPackageUpdater( monitor, _versionTag )?.CreateVersionResolver( ciBuild: true );
                }
                cache.Add( branch, resolver );
            }
            return resolver;
        };
    }
}
