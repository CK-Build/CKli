using CK.Core;
using CKli.Core;
using System.Collections.Immutable;

namespace CKli.HotZone.Plugin;

/// <summary>
/// Event raised by <see cref="HotZonePlugin.FixStartAsync"/>.
/// </summary>
public sealed class FixWorkflowStartEventArgs : WorldEventArgs
{
    readonly ImmutableArray<FixWorkflow.TargetRepo> _targets;
    readonly bool _restartingWorkflow;

    internal FixWorkflowStartEventArgs( IActivityMonitor monitor,
                                        CKliEnv context,
                                        World world,
                                        ImmutableArray<FixWorkflow.TargetRepo> targets,
                                        bool restartingWorkflow )
        : base( monitor, context, world )
    {
        _targets = targets;
        _restartingWorkflow = restartingWorkflow;
    }

    /// <summary>
    /// Gets whether the workflow already exists (current "ckli fix start" restarts it).
    /// </summary>
    public bool RestartingWorkflow => _restartingWorkflow;

    /// <summary>
    /// Gets the ordered fix roadmap.
    /// </summary>
    public ImmutableArray<FixWorkflow.TargetRepo> Targets => _targets;

}

