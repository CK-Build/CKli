using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    const string _dDepsAll = "Consider all the Repos, not only the current repositories.";
    const string _dDepsNarrow = "Keep the update to the current repositories and their upstreams: don't bring the downstreams of an updated repository in.";
    const string _dDepsNoFetch = "Don't fetch the repositories first. The analysis is then only as fresh as the last fetch.";
    const string _dDepsCI = "Consider the CI published profiles of the World References.";
    const string _dDepsPrerelease = "Consider the prerelease versions of the feeds even on the root branch.";
    const string _dDepsStable = "Consider only the stable versions of the feeds.";
    const string _dDepsAllowDowngrade = "Apply the updates that move a version down (a Reference may pin lower than what this World references).";

    /// <summary>
    /// Analyzes the external dependencies of a World and reports the upgrades that would align them.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The command context.</param>
    /// <param name="branch">The branch to consider.</param>
    /// <param name="all">True to consider all the Repos.</param>
    /// <param name="narrow">True to exclude the downstreams of an updated repository.</param>
    /// <param name="noFetch">True to skip the fetch.</param>
    /// <param name="ci">True to consider the CI published profiles of the References.</param>
    /// <param name="prerelease">True to consider the prereleases of the feeds.</param>
    /// <param name="stable">True to consider only the stable versions of the feeds.</param>
    /// <param name="allowDowngrade">True to apply the updates that move a version down.</param>
    /// <param name="dryRun">True to only display the upgrades.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Aligns the external package dependencies of a World on the versions its World References publish and,
        for the identifiers no reference anchors, on the greatest version its feeds offer.
        The repositories are updated on their "dev/" branch, which is created when it doesn't exist yet.
        Use --dry-run to only report what would be updated.
        """ )]
    [CommandPath( "deps update" )]
    public async Task<bool> DepsUpdateAsync( IActivityMonitor monitor,
                                             CKliEnv context,
                                             [Description( _dBranch )]
                                             [OptionName( _oBranch )]
                                             string? branch = null,
                                             [Description( _dDepsAll )]
                                             bool all = false,
                                             [Description( _dDepsNarrow )]
                                             bool narrow = false,
                                             [Description( _dDepsNoFetch )]
                                             bool noFetch = false,
                                             [Description( _dDepsCI )]
                                             bool ci = false,
                                             [Description( _dDepsPrerelease )]
                                             bool prerelease = false,
                                             [Description( _dDepsStable )]
                                             bool stable = false,
                                             [Description( _dDepsAllowDowngrade )]
                                             bool allowDowngrade = false,
                                             [Description( "Only display the upgrades." )]
                                             [OptionName( _oDryRun )]
                                             bool dryRun = false )
    {
        if( stable && prerelease )
        {
            monitor.Error( "--stable and --prerelease are exclusive." );
            return false;
        }
        var cancellation = PrimaryPluginContext.Cancellation;
        // The World must be coherent before anything is computed: no repository dirty (a commit would sweep
        // uncommitted work in), no version tag issue and no branch model issue. This is refused, never healed.
        if( !_hotZone.CheckBasicPreconditions( monitor, "updating the dependencies", out var allRepos ) )
        {
            return false;
        }
        // Consider the repositories selected by the current path as the Pivots.
        var pivots = all
                        ? allRepos
                        : World.GetAllDefinedRepo( monitor, context.CurrentDirectory, allowEmpty: false );
        if( pivots == null ) return false;

        // This command is online by design: it asks every feed for the versions it offers and reads the
        // References over http. Being stale about our own repositories while being fresh about the outside
        // world makes no sense, and it is what makes the divergence check below mean anything.
        if( !noFetch && !FetchAll( monitor, allRepos, cancellation ) )
        {
            return false;
        }
        if( !CheckNoRemoteDivergence( monitor, allRepos ) )
        {
            return false;
        }
        var branchName = ResolveBranchName( monitor, pivots, branch );
        if( branchName == null ) return false;

        var graph = _hotZone.GetHotGraph( monitor, branchName, isCIBuild: false, pivots );
        if( graph == null ) return false;

        var options = new UpgradeMap.Options( Narrow: narrow,
                                              ConsiderCI: ci,
                                              StableOnly: stable || (!prerelease && branchName.IsRoot) );
        var map = await UpgradeMap.CreateAsync( monitor, context, World, graph, _versionTag, _artifactHandler, options, cancellation )
                                  .ConfigureAwait( false );
        if( map == null ) return false;

        DisplayUpgrades( monitor, context, map );
        return dryRun || Apply( monitor, context, map, branchName, allowDowngrade );
    }

    // Writes the upgrades: the participants come upstream first (Upgrades is ordered by the solutions'
    // OrderedIndex). Everything here is repo-local.
    bool Apply( IActivityMonitor monitor, CKliEnv context, UpgradeMap map, BranchName branchName, bool allowDowngrade )
    {
        if( map.IsEmpty ) return true;
        if( map.DowngradeCount > 0 && !allowDowngrade )
        {
            monitor.Error( $"""
                {map.DowngradeCount} of these {map.UpgradeCount} updates move a version DOWN (▼ above). A World Reference
                may legitimately pin lower than what this World references - alignment is the point - but this is not
                applied unless --allow-downgrade is specified.
                """ );
            return false;
        }
        using( monitor.OpenInfo( $"Updating {map.Upgrades.Length} repositories on branch '{branchName}'." ) )
        {
            foreach( var r in map.Upgrades )
            {
                var repo = r.Repo;
                using( monitor.OpenInfo( $"Updating {r.Upgrades.Length} package(s) in '{repo.DisplayPath}'." ) )
                {
                    // The branch we analyzed: not necessarily the solution's own one, which is the closest
                    // existing branch when this repository doesn't have it yet.
                    var b = r.Solution.BranchInfo.Branches[branchName.Index];
                    // EnsureExists creates it at GetStartCommit: the very commit whose content has been
                    // analyzed. Synchronize is deliberately NOT called - a stale World has been refused,
                    // so there is nothing for it to do, and it is the only step that could move a tip
                    // away from what this report describes.
                    if( !b.Exists && !b.EnsureExists( monitor ) ) return false;
                    b.EnsureDevBranch();
                    // Development always takes place in the "dev/" branch.
                    if( !repo.GitRepository.Checkout( monitor, b.GitDevBranch ) ) return false;

                    var mapping = new PackageMapper();
                    foreach( var u in r.Upgrades )
                    {
                        // An exact mapping: only the version this repository actually references is rewritten.
                        mapping.Add( u.Current.PackageId, u.Current.Version, u.Target );
                    }
                    var updated = new PackageMapper();
                    if( !_solutionPlugin.UpdatePackages( monitor, repo, mapping, updated ) ) return false;
                    if( updated.Count != r.Upgrades.Length )
                    {
                        monitor.Warn( $"""
                            Expected {r.Upgrades.Length} package(s) to be updated in '{repo.DisplayPath}' but {updated.Count} were:
                            a reference may be centrally managed in a file this update doesn't reach.
                            """ );
                    }
                    if( !b.Commit( monitor, CreateCommitMessage( r ) ) ) return false;
                }
            }
        }
        context.Screen.Display( context.Screen.ScreenType.Text( $"""
            Updated {map.UpgradeCount} package(s) in {map.Upgrades.Length} repositories on '{branchName.DevName}'.
            Run 'ckli build' to rebuild them.
            """ ) );
        return true;
    }

    static string CreateCommitMessage( UpgradeMap.RepoUpgrades r )
    {
        var b = new System.Text.StringBuilder();
        b.Append( "chore: aligning " )
         .Append( r.Upgrades.Length )
         .AppendLine( r.Upgrades.Length == 1 ? " external dependency." : " external dependencies." )
         .AppendLine();
        foreach( var u in r.Upgrades )
        {
            b.Append( "- " ).Append( u.Current.PackageId ).Append( ' ' )
             .Append( u.Current.Version ).Append( " -> " ).AppendLine( u.Target.ToString() );
        }
        return b.ToString();
    }

    // A fetch updates the remote tracking references only: no branch moves and no content changes.
    bool FetchAll( IActivityMonitor monitor, IReadOnlyList<Repo> repos, CancellationToken cancellation )
    {
        using( monitor.OpenInfo( $"Fetching {repos.Count} repositories." ) )
        {
            bool success = true;
            foreach( var repo in repos )
            {
                success &= repo.GitRepository.FetchRemoteBranches( monitor, withTags: false, cancellation: cancellation );
            }
            return success;
        }
    }

    // A branch that is behind its tracked remote is refused, not merged: this command requires a coherent
    // World instead of producing one, so its report and what an apply would write are the same content.
    // Being ahead is fine: there is nothing to merge.
    static bool CheckNoRemoteDivergence( IActivityMonitor monitor, IReadOnlyList<Repo> repos )
    {
        List<string>? behind = null;
        foreach( var repo in repos )
        {
            foreach( var b in repo.GitRepository.Repository.Branches )
            {
                if( b.IsRemote || b.TrackedBranch == null ) continue;
                var d = b.TrackingDetails;
                if( d.BehindBy is > 0 )
                {
                    (behind ??= new List<string>()).Add( $"'{repo.DisplayPath}' branch '{b.FriendlyName}' is {d.BehindBy} commit(s) behind '{b.TrackedBranch.FriendlyName}'" );
                }
            }
        }
        if( behind != null )
        {
            monitor.Error( $"""
                The World is not up to date with its remotes, so an analysis of it would not describe what an update
                would write. Run 'ckli pull' first.
                {behind.Concatenate( Environment.NewLine )}
                """ );
            return false;
        }
        return true;
    }

    static void DisplayUpgrades( IActivityMonitor monitor, CKliEnv context, UpgradeMap map )
    {
        var screen = context.Screen.ScreenType;
        if( map.IsEmpty )
        {
            context.Screen.Display( screen.Text( $"""
                Nothing to update on branch '{map.Graph.BranchName}': the {map.Graph.Solutions.Count} repositories of this
                World already reference the {map.Targets.Count( t => t.HasTarget )} resolved external package(s) in their target version.
                """ ) );
            return;
        }
        // A multi line TextBlock trims each of its lines (a raw string literal carries its own indentation),
        // so this report cannot be one text: each line is its own renderable and the indented ones carry
        // their indentation as a left margin.
        var lines = new List<IRenderable>();
        var b = new System.Text.StringBuilder();
        b.Append( "Dependency upgrades of branch '" ).Append( map.Graph.BranchName ).Append( "'" );
        if( map.AnalysisOptions.Narrow ) b.Append( " (--narrow: upstreams only)" );
        b.Append( ':' );
        lines.Add( TakeLine( screen, b ) );
        foreach( var r in map.Upgrades )
        {
            b.Append( "- " ).Append( r.Repo.DisplayPath );
            if( r.IsPivot ) b.Append( " (pivot)" );
            if( r.NeedsBranch ) b.Append( $" [the '{map.Graph.BranchName}' branch would be created]" );
            lines.Add( TakeLine( screen, b ) );
            foreach( var u in r.Upgrades )
            {
                b.Append( u.IsDowngrade ? "▼ " : "▲ " )
                 .Append( u.Current.PackageId )
                 .Append( ' ' )
                 .Append( u.Current.Version )
                 .Append( " → " )
                 .Append( u.Target );
                var t = map.Targets.FirstOrDefault( x => x.PackageId.Equals( u.Current.PackageId, StringComparison.OrdinalIgnoreCase ) );
                if( t?.Origin != null ) b.Append( "  (" ).Append( t.Origin ).Append( ')' );
                lines.Add( TakeLine( screen, b ).Box( marginLeft: 4 ) );
            }
        }
        b.Append( map.UpgradeCount ).Append( " upgrade(s) in " ).Append( map.Upgrades.Length ).Append( " repositories" );
        if( map.DowngradeCount > 0 )
        {
            b.Append( ", including " ).Append( map.DowngradeCount ).Append( " downgrade(s) (▼)" );
        }
        b.Append( '.' );
        lines.Add( TakeLine( screen, b ) );
        var blocked = map.Targets.Where( t => t.State is UpgradeMap.TargetState.Conflict ).ToList();
        if( blocked.Count > 0 )
        {
            b.Append( blocked.Count ).Append( " package(s) are blocked by disagreeing World References:" );
            lines.Add( TakeLine( screen, b ) );
            foreach( var t in blocked )
            {
                b.Append( t.PackageId ).Append( ": " ).Append( t.Origin );
                lines.Add( TakeLine( screen, b ).Box( marginLeft: 4 ) );
            }
        }
        context.Screen.Display( screen.Unit.AddBelow( lines ) );

        // The builder's content becomes a single line block and the builder is reset.
        static TextBlock TakeLine( ScreenType screen, System.Text.StringBuilder b )
        {
            var line = screen.Text( b.ToString() );
            b.Clear();
            return line;
        }
    }

    // Same resolution as the build commands: the pivots' "dev/" stripped current branch, or --branch.
    BranchName? ResolveBranchName( IActivityMonitor monitor, IReadOnlyList<Repo> pivots, string? branch )
    {
        if( branch == null )
        {
            branch = pivots[0].GitStatus.CurrentBranchName;
            if( branch.StartsWith( "dev/", StringComparison.OrdinalIgnoreCase ) ) branch = branch.Substring( 4 );
            for( int i = 1; i < pivots.Count; ++i )
            {
                var other = pivots[i].GitStatus.CurrentBranchName;
                if( other.StartsWith( "dev/", StringComparison.OrdinalIgnoreCase ) ) other = other.Substring( 4 );
                if( other != branch )
                {
                    monitor.Error( $"""
                        Multiple Repo are selected and current checked out branches differ, the --branch <name> must be specified.
                        (At least, '{pivots[0].DisplayPath}' is on '{branch}' and '{pivots[i].DisplayPath}' is on '{other}'.)
                        """ );
                    return null;
                }
            }
            if( branch == "(no branch)" )
            {
                monitor.Error( $"""
                    A branch must be checked out or the --branch <name> must be specified.
                    (At least, '{pivots[0].DisplayPath}' is on detached head state).
                    """ );
                return null;
            }
            monitor.Info( ScreenType.CKliScreenTag, $"Selecting --branch '{branch}'." );
        }
        return _branchModel.BranchNamespace.FindRequired( monitor, branch );
    }
}
