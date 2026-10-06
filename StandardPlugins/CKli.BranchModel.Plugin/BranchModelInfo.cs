using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Immutable;
using System.Linq;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Branch related <see cref="RepoInfo"/>.
/// </summary>
public sealed partial class BranchModelInfo : RepoInfo
{
    readonly BranchNamespace _namespace;
    internal readonly BranchModelPlugin _plugin;

    // Deferred initialization.
    ImmutableArray<HotBranch> _branches;
    // The IssueKind.Useless is not considered here.
    bool _hasIssue;

    internal BranchModelInfo( Repo repo, BranchNamespace ns, BranchModelPlugin plugin )
        : base( repo )
    {
        _namespace = ns;
        _plugin = plugin;
    }

    internal void Initialize( ImmutableArray<HotBranch> branches, bool hasIssue )
    {
        _branches = branches;
        _hasIssue = hasIssue;
    }

    internal void SetHasIssue( bool hasIssue ) => _hasIssue = hasIssue;

    /// <summary>
    /// Gets the branch namespace.
    /// </summary>
    public BranchNamespace Namespace => _namespace;

    /// <summary>
    /// Gets all the <see cref="HotBranch"/> indexed by their <see cref="BranchName.Index"/>.
    /// Their git <see cref="HotBranch.GitBranch"/> may be null (<see cref="HotBranch.Exists"/> can be false).
    /// </summary>
    public ImmutableArray<HotBranch> Branches => _branches;

    /// <summary>
    /// Gets the root branch.
    /// Its <see cref="HotBranch.GitBranch"/> can be null (this has to be fixed, <see cref="HasIssue"/> is true, before doing almost
    /// anything in this Repo).  
    /// </summary>
    public HotBranch Root => _branches[0];

    /// <summary>
    /// Gets the closest <see cref="HotBranch"/> with a non null <see cref="HotBranch.GitBranch"/> in the <see cref="Namespace"/>.
    /// <para>
    /// This is null only if the root "stable" branch is missing.
    /// </para>
    /// </summary>
    /// <param name="name">The starting branch.</param>
    /// <returns>The branch to consider.</returns>
    public HotBranch? GetClosestExistingBranch( BranchName name )
    {
        var b = _branches[name.Index];
        do
        {
            if( b.GitBranch != null ) return b;
            b = b.Parent;
        }
        while( b != null );
        return null;
    }

    /// <summary>
    /// Gets the closest <see cref="HotBranch"/> with a non null <see cref="HotBranch.GitBranch"/> in the <see cref="Namespace"/>
    /// or emits an error when the root branch is missing or when a skipped branch <see cref="HotBranch.HasOrphanDevBranch"/>:
    /// its "dev/" branch carries work that the closest existing branch doesn't have.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="name">The branch name from which the closest existing branch must be found.</param>
    /// <returns>The branch (that may be the <paramref name="name"/> one) or null.</returns>
    public HotBranch? GetRequiredClosestExistingBranch( IActivityMonitor monitor, BranchName name )
    {
        if( !CheckRoot( monitor ) ) return null;
        var b = GetClosestExistingBranch( name );
        Throw.DebugAssert( "The root exists.", b != null );
        // Stops on this issue because it will introduce an ambiguity. Every skipped branch is considered: an orphan
        // "dev/" branch of a grand parent is as ambiguous as the parent's one (by design, b cannot have an orphan
        // "dev/" branch because its GitBranch exists).
        for( var skipped = _branches[name.Index]; skipped != b; skipped = skipped.Parent! )
        {
            if( skipped.HasOrphanDevBranch )
            {
                var n = skipped.BranchName;
                monitor.Error( $"""
                        Branch '{n.DevName}' in '{Repo.DisplayPath}' exists but its base '{n.Name}' branch doesn't exist.
                        Use 'ckli issue --fix' to recreate it.
                        """ );
                return null;
            }
        }
        return b;
    }

    /// <summary>
    /// Checks that the root branch exists or emits an error. A missing root branch is the ultimate branch issue: the
    /// <see cref="Branches"/> only contain the <see cref="Root"/> and nothing can be done in this repository until
    /// "ckli issue --fix" solves it.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True if the root branch exists, false otherwise.</returns>
    public bool CheckRoot( IActivityMonitor monitor )
    {
        if( Root.GitBranch != null ) return true;
        monitor.Error( $"Missing root '{_namespace.Root.Name}' branch in '{Repo.DisplayPath}'. Use 'ckli issue --fix' to fix this." );
        return false;
    }

    internal ShallowSolutionPlugin ShallowSolutionPlugin => _plugin._shallowSolution;

    /// <inheritdoc />
    /// <remarks>
    /// The "dev/" branch can be <see cref="BranchLink.IssueKind.Useless"/> here.
    /// This minor issue is collected by "ckli issue" but is not treated as a real issue.
    /// </remarks>
    public override bool HasIssue => _hasIssue;

    internal void CollectIssues( IActivityMonitor monitor,
                                 ScreenType screenType,
                                 Action<World.Issue> collector,
                                 bool forgetUselessBranches,
                                 bool autoFixDevBranch,
                                 out bool hasSevereIssues )
    {
        // If the "stable" branch doesn't exist, no need to continue.
        if( Root.GitBranch == null )
        {
            // Use "dev/stable" if it exists.
            Branch? prevRoot = Root.GitDevBranch ?? _namespace.GetPreviousRootBranch( monitor, Repo.GitRepository );
            collector( MissingRootBranchIssue.Create( _namespace, Root, prevRoot, screenType ) );
            hasSevereIssues = true;
            return;
        }
        var issues = new BranchIssueBuilder( forgetUselessBranches, autoFixDevBranch );
        foreach( var b in _branches )
        {
            b.Collect( issues );
        }
        issues.CollectIssues( monitor, Repo, screenType, collector, out hasSevereIssues );
    }

}
