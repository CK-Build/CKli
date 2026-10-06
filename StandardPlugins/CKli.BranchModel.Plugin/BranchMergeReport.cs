using CKli.Core;
using System;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Collects, across the repositories of a branch command ("ckli branch open", "sync" and "close"), what its merges do
/// or, in a dry run, would do: the commands display it once at their end (see <see cref="Display"/>).
/// <para>
/// The <see cref="HotBranch"/> operations that receive it (<see cref="HotBranch.OpenOrPredict"/>,
/// <see cref="HotBranch.SynchronizeOrPredict"/> and <see cref="HotBranch.CloseOrPredict"/>) predict instead of acting
/// when <see cref="DryRun"/> is true.
/// </para>
/// </summary>
sealed class BranchMergeReport
{
    readonly List<MergeOutcome> _outcomes;
    readonly List<PreparedMerge> _preparedMerges;
    readonly Action<PreparedMerge>? _onPreparedMerge;
    readonly bool _dryRun;
    int _creations;

    /// <summary>
    /// Initializes a new report.
    /// </summary>
    /// <param name="dryRun">True to predict the merges instead of doing them.</param>
    /// <param name="prepareMerges">
    /// True to leave a merge that conflicts beyond the package versions in progress in the working folder: this requires
    /// a package version resolver (see <see cref="HotBranch.Synchronize"/>).
    /// </param>
    public BranchMergeReport( bool dryRun, bool prepareMerges )
    {
        _dryRun = dryRun;
        _outcomes = new List<MergeOutcome>();
        _preparedMerges = new List<PreparedMerge>();
        _onPreparedMerge = prepareMerges ? _preparedMerges.Add : null;
    }

    /// <summary>
    /// Gets whether nothing is changed: the merges are predicted.
    /// </summary>
    public bool DryRun => _dryRun;

    /// <summary>
    /// Gets the callback that receives a merge left in progress. Null when the merges are not prepared: a conflict
    /// simply fails.
    /// </summary>
    public Action<PreparedMerge>? OnPreparedMerge => _onPreparedMerge;

    /// <summary>
    /// Collects a predicted merge.
    /// </summary>
    /// <param name="outcome">The predicted outcome.</param>
    /// <param name="conflict">The merge that would be left in progress when the outcome is <see cref="MergeOutcome.Conflict"/>.</param>
    /// <returns>What the operation would return: false on a conflict or a failure.</returns>
    public bool OnPrediction( MergeOutcome outcome, PreparedMerge? conflict )
    {
        _outcomes.Add( outcome );
        if( conflict != null ) _preparedMerges.Add( conflict );
        return outcome is not (MergeOutcome.Conflict or MergeOutcome.Failed);
    }

    /// <summary>
    /// Collects a predicted branch creation.
    /// </summary>
    public void OnCreationPrediction() => ++_creations;

    /// <summary>
    /// Displays the result of a dry run (see <see cref="PreparedMerge.DisplayDryRun"/>) or the merges left in progress
    /// (see <see cref="PreparedMerge.Display"/>).
    /// </summary>
    /// <param name="screen">The screen.</param>
    /// <param name="closedBranch">The branch that is closed when the merges are left by "ckli branch close".</param>
    public void Display( IScreen screen, string? closedBranch = null )
    {
        if( _dryRun )
        {
            PreparedMerge.DisplayDryRun( screen,
                                         _outcomes,
                                         _preparedMerges,
                                         actions: _creations switch
                                         {
                                             0 => null,
                                             1 => ["1 creation"],
                                             _ => [$"{_creations} creations"]
                                         } );
        }
        else
        {
            PreparedMerge.Display( screen, _preparedMerges, closedBranch );
        }
    }
}
