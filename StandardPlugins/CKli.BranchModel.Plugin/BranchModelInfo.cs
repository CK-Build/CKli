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
    static readonly string[] _autoPrevRootBranchNames = ["stable", "main", "master", "root", "trunk", "mother", "primary", "develop"];

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
    /// that is not <see cref="HotBranch.HasOrphanDevBranch"/> or emits an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="name">The branch name from which the closest existing branch must be found.</param>
    /// <returns>The branch (that may be the <paramref name="name"/> one) or null.</returns>
    public HotBranch? GetRequiredClosestExistingBranch( IActivityMonitor monitor, BranchName name )
    {
        var b = GetClosestExistingBranch( name );
        if( b == null )
        {
            monitor.Error( $"Missing root '{_namespace.Root.Name}' branch in '{Repo.DisplayPath}'. Please create it or use 'ckli issue' to fix this." );
            return null;
        }
        // Stops on this issue because it will introduce an ambiguity.
        // This applies only to a "real" closest branch (by design, if b is the requested name branch, it cannot has an orphan dev
        // branch because its GitBranch exists).
        if( b.BranchName != name && _branches[name.Index].HasOrphanDevBranch )
        {
            monitor.Error( $"""
                    Branch '{name.DevName}' in '{Repo.DisplayPath}' exists but its base '{name.Name}' branch doesn't exist.
                    Please remove '{name.DevName}' branch or use 'ckli issue' to fix this.
                    """ );
            return null;
        }
        return b;
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
                                 out bool hasSevereIssues )
    {
        // If the "stable" branch doesn't exist, no need to continue.
        if( Root.GitBranch == null )
        {
            // Use "dev/stable" if it exists.
            Branch? prevRoot = Root.GitDevBranch
                                    ?? _autoPrevRootBranchNames.Where( n => !n.Equals( _namespace.Root.Name, StringComparison.OrdinalIgnoreCase ) )
                                                                   .Select( n => Repo.GitRepository.GetBranch( monitor, n, LogLevel.Info ) )
                                                                   .FirstOrDefault( b => b != null );
            collector( MissingRootBranchIssue.Create( monitor, Root, prevRoot, screenType ) );
            hasSevereIssues = true;
            return;
        }
        var issues = new BranchIssueBuilder();
        foreach( var b in _branches )
        {
            b.Collect( issues );
        }
        issues.CollectIssues( monitor, Repo, screenType, collector, out hasSevereIssues );
    }

}
