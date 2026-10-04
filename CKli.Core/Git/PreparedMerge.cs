using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// A merge that conflicts: either left in progress in the working folder of a repository, for a person to resolve its
/// conflicts and commit it (see <see cref="GitRepository.PrepareMerge"/>), or not merged at all (see <see cref="DisplayNotMerged"/>).
/// </summary>
/// <param name="Repo">The repository.</param>
/// <param name="TargetBranch">The branch that receives the merge: it is checked out when the merge is in progress.</param>
/// <param name="Merged">What is merged: "branch 'X'" or "commit 'sha message'".</param>
/// <param name="Conflicts">The paths that are in conflict.</param>
public sealed record PreparedMerge( Repo Repo, string TargetBranch, string Merged, IReadOnlyList<string> Conflicts )
{
    /// <summary>
    /// Gets a <see cref="Collapsable"/> with the repository (linked to its working folder), the checked out branch,
    /// what is merged and the number of conflicts above the paths in conflict.
    /// </summary>
    /// <param name="s">The screen type.</param>
    /// <returns>The renderable.</returns>
    public IRenderable ToRenderable( ScreenType s )
    {
        var header = Repo.ToLinkedNameRenderable( s, TextStyle.None )
                         .AddRight( s.Text( $"⎇ {TargetBranch}" ).Box( marginLeft: 2 ),
                                    s.Text( $"← {Merged}", ConsoleColor.DarkGray ).Box( marginLeft: 1 ),
                                    s.Text( Conflicts.Count == 1 ? "1 conflict" : $"{Conflicts.Count} conflicts", ConsoleColor.Red ).Box( marginLeft: 2 ) );
        var paths = s.Unit.AddBelow( Conflicts.Select( p => s.Text( p, ConsoleColor.Yellow ) ) );
        return new Collapsable( header.AddBelow( paths ) );
    }

    /// <summary>
    /// Displays the merges left in progress, if any, with what remains to be done.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <param name="merges">The merges left in progress.</param>
    /// <param name="closedBranch">The branch that is closed when the merges are left by "ckli branch close": it must be closed again.</param>
    public static void Display( IScreen screen, IReadOnlyList<PreparedMerge> merges, string? closedBranch = null )
    {
        if( merges.Count == 0 ) return;
        var s = screen.ScreenType;
        var (its, it) = merges.Count == 1 ? ("its", "it") : ("their", "them");
        var what = closedBranch == null
                    ? $"resolve {its} conflicts and commit {it} (or abort {it})."
                    : $"resolve {its} conflicts, commit {it} (or abort {it}) and close '{closedBranch}' again.";
        screen.Display( s.Text( $"{(merges.Count == 1 ? "A merge is" : $"{merges.Count} merges are")} left in progress: {what}" )!
                         .AddBelow( merges.Select( m => m.ToRenderable( s ) ) ) );
    }

    /// <summary>
    /// Displays the merges that conflict and are not merged, if any: their branch keeps its own commits and doesn't
    /// receive the remote ones.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <param name="merges">The merges that are not done.</param>
    public static void DisplayNotMerged( IScreen screen, IReadOnlyList<PreparedMerge> merges )
    {
        if( merges.Count == 0 ) return;
        var s = screen.ScreenType;
        screen.Display( s.Text( $"{(merges.Count == 1 ? "A merge conflicts and is" : $"{merges.Count} merges conflict and are")} not merged: 'ckli pull --branch <name>' leaves the merge of the branch you work on in progress." )!
                         .AddBelow( merges.Select( m => m.ToRenderable( s ) ) ) );
    }

    /// <summary>
    /// Displays the result of a dry run: the count of each outcome, the merges that would be left in progress and the
    /// ones that would not be merged.
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <param name="outcomes">The predicted outcomes, one per merge.</param>
    /// <param name="conflicts">The merges that would be left in progress.</param>
    /// <param name="notMerged">The merges that would conflict and not be merged.</param>
    public static void DisplayDryRun( IScreen screen,
                                      IReadOnlyList<MergeOutcome> outcomes,
                                      IReadOnlyList<PreparedMerge> conflicts,
                                      IReadOnlyList<PreparedMerge>? notMerged = null )
    {
        var s = screen.ScreenType;
        var counts = new List<string>();
        Add( counts, outcomes, MergeOutcome.Merge, "merge", "merges" );
        Add( counts, outcomes, MergeOutcome.FastForward, "fast-forward", "fast-forwards" );
        Add( counts, outcomes, MergeOutcome.UpToDate, "up to date", "up to date" );
        Add( counts, outcomes, MergeOutcome.Conflict, "conflict", "conflicts" );
        Add( counts, outcomes, MergeOutcome.Failed, "failure", "failures" );
        var summary = counts.Count == 0 ? "nothing to do" : counts.Concatenate( ", " );
        IRenderable display = s.Text( $"Dry run: {summary}. Nothing has been changed." )!;
        if( conflicts.Count > 0 )
        {
            display = display.AddBelow( s.Text( $"{(conflicts.Count == 1 ? "A merge would be" : $"{conflicts.Count} merges would be")} left in progress:" ),
                                        s.Unit.AddBelow( conflicts.Select( m => m.ToRenderable( s ) ) ) );
        }
        if( notMerged is { Count: > 0 } )
        {
            display = display.AddBelow( s.Text( $"{(notMerged.Count == 1 ? "A merge would conflict and not be" : $"{notMerged.Count} merges would conflict and not be")} merged:" ),
                                        s.Unit.AddBelow( notMerged.Select( m => m.ToRenderable( s ) ) ) );
        }
        screen.Display( display );

        static void Add( List<string> counts, IReadOnlyList<MergeOutcome> outcomes, MergeOutcome outcome, string one, string many )
        {
            int n = outcomes.Count( o => o == outcome );
            if( n > 0 ) counts.Add( $"{n} {(n == 1 ? one : many)}" );
        }
    }
}
