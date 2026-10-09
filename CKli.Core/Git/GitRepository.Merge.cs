using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Rewrites the two sides of a merge that conflicts so that the conflicts it knows how to resolve disappear: the
/// rewritten commits are children of their original commit (the merge base stays the same) and only exist in the
/// object database. The merge that follows merges them, and its commit has the two ORIGINAL commits as parents.
/// <para>
/// An aligner that has nothing to do outputs the original commits. This is how a plugin takes part in a merge
/// without Core knowing what it aligns (package versions, for instance).
/// </para>
/// </summary>
/// <param name="monitor">The monitor to use.</param>
/// <param name="ours">The commit that receives the merge.</param>
/// <param name="theirs">The commit to merge.</param>
/// <param name="conflicts">The paths that conflict when merging the original commits.</param>
/// <param name="mustResolveAll">
/// True when the merge will be committed only if no conflict remains: an aligner that cannot resolve every one of
/// the <paramref name="conflicts"/> can skip its work (and output the original commits).
/// </param>
/// <param name="oursAligned">Outputs the commit to merge instead of <paramref name="ours"/>.</param>
/// <param name="theirsAligned">Outputs the commit to merge instead of <paramref name="theirs"/>.</param>
/// <returns>True on success, false on error (the error is logged).</returns>
public delegate bool MergeSidesAligner( IActivityMonitor monitor,
                                        Commit ours,
                                        Commit theirs,
                                        IReadOnlyList<string> conflicts,
                                        bool mustResolveAll,
                                        out Commit oursAligned,
                                        out Commit theirsAligned );

public sealed partial class GitRepository
{
    /// <summary>
    /// Predicts the merge of <paramref name="other"/> into <paramref name="target"/> that
    /// <see cref="CreateMergeCommit"/> (or <see cref="PrepareMerge"/>) would do. Like the merge itself, this creates
    /// objects in the object database only (they are left unreferenced): no branch moves, the working folder is not
    /// touched.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="target">The commit that receives the merge (the tip of the target branch).</param>
    /// <param name="other">The commit to merge.</param>
    /// <param name="aligner">The optional aligner of the two sides, called only when the merge conflicts.</param>
    /// <param name="conflicts">Outputs the paths that would be in conflict when the outcome is <see cref="MergeOutcome.Conflict"/>.</param>
    /// <returns>The outcome.</returns>
    public MergeOutcome PredictMerge( IActivityMonitor monitor,
                                      Commit target,
                                      Commit other,
                                      MergeSidesAligner? aligner,
                                      out IReadOnlyList<string> conflicts )
    {
        conflicts = [];
        var d = _git.ObjectDatabase.CalculateHistoryDivergence( other, target );
        if( d.AheadBy is 0 ) return MergeOutcome.UpToDate;
        if( d.BehindBy is 0 ) return MergeOutcome.FastForward;
        // Same content: no merge is needed (see MergeBranchContent), unless other brings a tagged commit.
        if( target.Tree.Sha == other.Tree.Sha ) return BringsTaggedCommit( target, other ) ? MergeOutcome.Merge : MergeOutcome.UpToDate;
        var merge = MergeTrees( monitor, target, other, aligner, mustResolveAll: false );
        if( merge.Failed ) return MergeOutcome.Failed;
        conflicts = merge.Conflicts;
        return conflicts.Count == 0 ? MergeOutcome.Merge : MergeOutcome.Conflict;
    }

    /// <summary>
    /// Creates the merge commit of <paramref name="theirs"/> into <paramref name="ours"/>, aligning the two sides with
    /// the <paramref name="aligner"/> when the merge conflicts. This creates objects in the object database only: no
    /// branch moves, the working folder is not touched.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="ours">The commit that receives the merge.</param>
    /// <param name="theirs">The commit to merge.</param>
    /// <param name="theirsName">What is merged, for the merge commit message "Merged {theirsName}." ("branch 'X'" or "commit 'sha message'").</param>
    /// <param name="aligner">The optional aligner of the two sides, called only when the merge conflicts.</param>
    /// <param name="conflicts">Outputs the paths that are in conflict when null is returned and no error occurred.</param>
    /// <returns>The merge commit or null if the merge conflicts (<paramref name="conflicts"/> is not empty) or on error (it is empty).</returns>
    public Commit? CreateMergeCommit( IActivityMonitor monitor,
                                      Commit ours,
                                      Commit theirs,
                                      string theirsName,
                                      MergeSidesAligner? aligner,
                                      out IReadOnlyList<string> conflicts )
    {
        conflicts = [];
        var merge = MergeTrees( monitor, ours, theirs, aligner, mustResolveAll: true );
        if( merge.Failed ) return null;
        if( merge.Conflicts.Count > 0 )
        {
            conflicts = merge.Conflicts;
            return null;
        }
        return _git.ObjectDatabase.CreateCommit( Author, Committer, $"Merged {theirsName}.", merge.Tree!, [ours, theirs], prettifyMessage: true );
    }

    /// <summary>
    /// Leaves the merge of <paramref name="theirs"/> into the <paramref name="target"/> branch in progress in the
    /// working folder, for the conflicts that remain once the sides are aligned (see <see cref="MergeSidesAligner"/>):
    /// a "merge in progress" that a person resolves and commits with any Git tool.
    /// <para>
    /// The <paramref name="target"/> must be checked out and the working folder must be clean. The aligned target is
    /// checked out (detached) and the aligned <paramref name="theirs"/> is merged without committing: the working
    /// folder and the index hold the merge, with conflict markers only where the conflicts are real. The HEAD is then
    /// the <paramref name="target"/> branch again (the index and the working folder are not touched) and MERGE_HEAD is
    /// the original <paramref name="theirs"/>: the merge commit that the person creates has the two original commits
    /// as parents. Aborting the merge ("git merge --abort") restores the <paramref name="target"/> branch as it was.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="target">The checked out branch that receives the merge.</param>
    /// <param name="theirs">The commit to merge.</param>
    /// <param name="theirsName">What is merged, for the merge message "Merged {theirsName}." ("branch 'X'" or "commit 'sha message'").</param>
    /// <param name="aligner">The optional aligner of the two sides.</param>
    /// <param name="conflicts">Outputs the paths that are in conflict in the working folder.</param>
    /// <returns>True when the merge is in progress, false on error.</returns>
    public bool PrepareMerge( IActivityMonitor monitor,
                              Branch target,
                              Commit theirs,
                              string theirsName,
                              MergeSidesAligner? aligner,
                              out IReadOnlyList<string> conflicts )
    {
        Throw.CheckArgument( target.IsCurrentRepositoryHead );
        conflicts = [];
        if( !CheckCleanCommit( monitor ) ) return false;
        var ours = target.Tip;
        Commit oursAligned = ours;
        Commit theirsAligned = theirs;
        if( aligner != null )
        {
            var first = _git.ObjectDatabase.MergeCommits( ours, theirs, new MergeTreeOptions { SkipReuc = true } );
            if( first.Status == MergeTreeStatus.Conflicts
                && !aligner( monitor, ours, theirs, GetConflictPaths( first ), mustResolveAll: false, out oursAligned, out theirsAligned ) )
            {
                return false;
            }
        }
        try
        {
            Commands.Checkout( _git, oursAligned );
            var result = _git.Merge( theirsAligned,
                                     Committer,
                                     new MergeOptions { CommitOnSuccess = false, FastForwardStrategy = FastForwardStrategy.NoFastForward } );
            if( result.Status != MergeStatus.Conflicts )
            {
                monitor.Error( $"Unexpected merge status '{result.Status}' while preparing the merge of {theirsName} into '{target.FriendlyName}' in '{DisplayPath}'." );
                Commands.Checkout( _git, target, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force } );
                return false;
            }
            // HEAD is the target branch again: only the reference changes, the index and the working folder
            // hold the merge. MERGE_HEAD, MERGE_MSG and ORIG_HEAD are the ones of a merge of the original commits.
            _git.Refs.UpdateTarget( _git.Refs.Head, _git.Refs[target.CanonicalName] );
            var gitFolder = _git.Info.Path;
            File.WriteAllText( Path.Combine( gitFolder, "MERGE_HEAD" ), theirs.Sha + "\n" );
            File.WriteAllText( Path.Combine( gitFolder, "ORIG_HEAD" ), ours.Sha + "\n" );
            File.WriteAllText( Path.Combine( gitFolder, "MERGE_MSG" ), $"Merged {theirsName}.\n" );
            conflicts = _git.Index.Conflicts.Select( c => (c.Ours ?? c.Theirs ?? c.Ancestor).Path ).Distinct().ToList();
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While preparing the merge of {theirsName} into '{target.FriendlyName}' in '{DisplayPath}'.", ex );
            return false;
        }
    }

    // Merges the trees of the two commits, aligning the sides when they conflict and an aligner is available.
    (Tree? Tree, IReadOnlyList<string> Conflicts, bool Failed) MergeTrees( IActivityMonitor monitor,
                                                                          Commit ours,
                                                                          Commit theirs,
                                                                          MergeSidesAligner? aligner,
                                                                          bool mustResolveAll )
    {
        var db = _git.ObjectDatabase;
        var merge = db.MergeCommits( ours, theirs, new MergeTreeOptions { SkipReuc = true } );
        if( merge.Status == MergeTreeStatus.Conflicts && aligner != null )
        {
            if( !aligner( monitor, ours, theirs, GetConflictPaths( merge ), mustResolveAll, out var oursAligned, out var theirsAligned ) )
            {
                return (null, [], true);
            }
            if( oursAligned != ours || theirsAligned != theirs )
            {
                merge = db.MergeCommits( oursAligned, theirsAligned, new MergeTreeOptions { SkipReuc = true } );
            }
        }
        return merge.Status == MergeTreeStatus.Conflicts
                ? (null, GetConflictPaths( merge ), false)
                : (merge.Tree, [], false);
    }

    // A renamed file appears with its old and its new path.
    static IReadOnlyList<string> GetConflictPaths( MergeTreeResult merge )
    {
        return merge.Conflicts.SelectMany( c => new[] { c.Ancestor, c.Ours, c.Theirs } )
                              .Where( e => e != null )
                              .Select( e => e.Path )
                              .Distinct()
                              .ToList();
    }
}
