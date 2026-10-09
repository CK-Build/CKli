using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;

namespace CKli.HotZone.Plugin;

// "ckli branch open", "ckli branch switch", "ckli branch sync", "ckli branch close" and "ckli branch list" are implemented
// by the BranchModel plugin: they are handled here because the package versions that conflict when merging a branch's
// link are resolved like a build of the branch would update them, and that needs the branch's HotGraph.
public sealed partial class HotZonePlugin
{
    /// <summary>
    /// Switches the working folder to the specified branch, optionally opening it in the repositories.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The minimal CKli context.</param>
    /// <param name="branch">The branch name to switch to.</param>
    /// <param name="create">True to open the branch in the repositories where it doesn't exist and synchronize it.</param>
    /// <param name="all">Consider all the Repos of the current World.</param>
    /// <returns>True on success, false otherwise.</returns>
    [Description( """
        Switch the working folder to the specified branch of the branch model, optionally opening it in the Repo.
        Without --create, the branch (its "dev/" branch when it exists) or its closest existing branch is checked out.
        With --create, the branch is opened like "ckli branch open" opens an already opened branch: created if needed,
        synchronized with its parent and its "dev/" branch is checked out. The package versions that conflict are
        resolved the way a build of the branch updates them; when other conflicts remain, the merge is left in progress.
        """ )]
    [CommandPath( "branch switch" )]
    public bool BranchSwitch( IActivityMonitor monitor,
                              CKliEnv context,
                              [Description( "Branch name to checkout." )]
                              string branch,
                              [Description( "Open (create and synchronize) the branch, instead of switching to the closest existing one when it doesn't exist." )]
                              [OptionName( "--create,-c" )]
                              bool create = false,
                              [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                              bool all = false )
    {
        return _branchModel.SwitchBranch( monitor, context, branch, create, all, CreateVersionResolverProvider() );
    }

    /// <summary>
    /// Opens a Conformant SVersion branch, or updates the link type of an opened one, in the current repositories.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branchName">The branch name to open.</param>
    /// <param name="link">Optional link type (Manual, Regular, CI or Full) to the parent branch.</param>
    /// <param name="parent">Parent branch to consider instead of the currently checked out branch (applies only to 'explo/' branch).</param>
    /// <param name="dryRun">True to only display what the open would do.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Opens a Conformant SVersion branch if it doesn't already exist, and synchronizes it with its parent.
        - For prerelease branches ('alpha', 'bravo', 'charlie', ...'zulu'), the parent branch is based on the lexicographic order.
        - For exploratory branches ('explo/name'), the parent is the currently checked out branch (unless --parent option specifies it).
        A new branch starts at the commit that its link propagates: only an already opened branch can have something to merge.
        The package versions that conflict are resolved the way a build of the branch updates them. When other conflicts
        remain, the merge is left in progress in the working folder (the "dev/" branch is checked out): resolve them and
        commit the merge.
        With --dry-run, nothing is created nor merged: what the open would do is displayed and the command fails when it
        would fail (a merge would be left in progress, for instance).
        """ )]
    [CommandPath( "branch open" )]
    public bool BranchOpen( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Branch name to open." )]
                            string branchName,
                            [Description( """
                                Specifies the link (Manual, Regular, CI or Full) to the parent branch.
                                Defaults to Full for a new branch: an already opened branch keeps its current link type.
                                """ )]
                            [OptionName( "--link,-l" )]
                            string? link = null,
                            [Description( "Parent branch to consider instead of the currently checked out branch (applies only to 'explo/' branch)." )]
                            string? parent = null,
                            [Description( "Displays what the open would do without creating nor merging anything." )]
                            [OptionName( "--dry-run,-d" )]
                            bool dryRun = false )
    {
        return _branchModel.OpenBranch( monitor, context, branchName, link, parent, CreateVersionResolverProvider(), dryRun );
    }

    /// <summary>
    /// Synchronizes the specified branch with its closest parent branch.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branch">The branch name to synchronize.</param>
    /// <param name="link">Optional link type (Regular, CI or Full) to synchronize with instead of the configured one.</param>
    /// <param name="all">Consider all the Repos of the current World (even if current path is in a Repo).</param>
    /// <param name="dryRun">True to only display what the synchronization would do.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Synchronize the specified branch with its closest parent branch.
        The package versions that conflict are resolved the way a build of the branch updates them. When other conflicts
        remain, the merge is left in progress in the working folder (the "dev/" branch is checked out): resolve them and
        commit the merge.
        With --dry-run, nothing is merged: what the synchronization would do is displayed and the command fails when it
        would fail (a merge would be left in progress, for instance).
        """ )]
    [CommandPath( "branch sync" )]
    public bool BranchSync( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Branch name to synchronize." )]
                            string branch,
                            [Description( "Link type (Regular, CI or Full) to synchronize with instead of the configured one." )]
                            [OptionName( "--link,-l" )]
                            string? link = null,
                            [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                            bool all = false,
                            [Description( "Displays what the synchronization would do without merging anything." )]
                            [OptionName( "--dry-run,-d" )]
                            bool dryRun = false )
    {
        return _branchModel.SynchronizeBranch( monitor, context, branch, link, all, CreateVersionResolverProvider(), dryRun );
    }

    /// <summary>
    /// Closes a Conformant SVersion branch in the current repositories by integrating it in the "dev/" branch of its
    /// closest parent.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="branchName">The branch name to close.</param>
    /// <param name="discard">True to keep the branch where it is and not integrate it in its closest parent.</param>
    /// <param name="dryRun">True to only display what the close would do.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Closes a Conformant SVersion branch in the current repositories by integrating it in the "dev/" branch of its
        closest parent: the parent's base branch receives it when its "dev/" branch is integrated.
        The upstreams whose versions of this branch are consumed are also closed. The branch is removed from the
        branch model when no repository of the World has it anymore.
        The package versions that conflict are resolved the way a build of the parent updates them. When other conflicts
        remain, the merge is left in progress in the working folder: resolve them, commit the merge and close the branch
        again.
        With --dry-run, nothing is merged nor deleted: what the close would do is displayed and the command fails when it
        would fail (a merge would be left in progress, for instance).
        """ )]
    [CommandPath( "branch close" )]
    public bool BranchClose( IActivityMonitor monitor,
                             CKliEnv context,
                             [Description( "Branch name to close." )]
                             string branchName,
                             [Description( "Only remove the branch from the branch model (must be run at the root of the World): the Git branches are left as-is." )]
                             bool discard = false,
                             [Description( "Displays what the close would do without merging nor deleting anything." )]
                             [OptionName( "--dry-run,-d" )]
                             bool dryRun = false )
    {
        return _branchModel.CloseBranch( monitor, context, branchName, discard, CreateVersionResolverProvider(), dryRun );
    }

    /// <summary>
    /// Displays the opened branches of the World, the repositories where each of them has changes and what
    /// "ckli branch close" and "ckli branch sync" would merge.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The minimal context.</param>
    /// <param name="link">Optional link type (Regular, CI or Full) that the synchronization predictions consider instead of the configured ones.</param>
    /// <returns>True on success, false if the link is invalid or a solution cannot be read.</returns>
    [Description( """
        Displays the opened branches of the World and how each one is linked to its parent.
        For each branch, the repositories where it has changes are summarized by their tips (the ones that have no
        upstream repository where the branch has changes), the number of repositories where the branch is opened
        without changes and the weight of the branch: the number of repositories and projects that a build of the
        branch touches (the ones with changes and all their downstreams).
        Between a branch and its parent, "↖" tells what "ckli branch close" would merge into the parent and "↘" what
        "ckli branch sync" would merge into the branch: the number of fast-forwards and merges, and the repositories
        where the merge conflicts.
        """ )]
    [CommandPath( "branch list" )]
    public bool BranchList( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Link type (Regular, CI or Full) that the synchronization predictions consider instead of the configured ones." )]
                            [OptionName( "--link,-l" )]
                            string? link = null )
    {
        return _branchModel.DisplayBranchList( monitor, context, link, CreateVersionResolverProvider() );
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
                    resolver = graph?.CreateMergeVersionResolver( monitor, _versionTag, ciBuild: true );
                }
                cache.Add( branch, resolver );
            }
            return resolver;
        };
    }
}
