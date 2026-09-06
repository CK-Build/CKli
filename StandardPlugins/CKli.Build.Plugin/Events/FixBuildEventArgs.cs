using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.HotZone.Plugin;
using System.Collections.Immutable;

namespace CKli.Build.Plugin;

/// <summary>
/// Event raised by <see cref="BuildPlugin"/> when a <see cref="FixWorkflow"/> has been successfully built.
/// </summary>
public sealed class FixBuildEventArgs : BuildBaseEventArgs
{
    readonly FixWorkflow _fix;
    readonly ImmutableArray<BuildResult> _results;
    readonly bool _keepBranchOnSuccessfulPublish;

    internal FixBuildEventArgs( IActivityMonitor monitor,
                                CKliEnv context,
                                FixWorkflow fix,
                                ImmutableArray<BuildResult> results,
                                bool shouldPublish,
                                bool keepBranchOnSuccessfulPublish )
        : base( monitor, context, fix.World, shouldPublish )
    {
        _fix = fix;
        _results = results;
        _keepBranchOnSuccessfulPublish = keepBranchOnSuccessfulPublish;
    }

    /// <summary>
    /// Gets the fix that has been built.
    /// </summary>
    public FixWorkflow FixWorkflow => _fix;

    /// <summary>
    /// Gets the build results.
    /// </summary>
    public ImmutableArray<BuildResult> Results => _results;

    /// <summary>
    /// Gets whether the "fix/" branches must be kept once the fix is published.
    /// </summary>
    public bool KeepBranchOnSuccessfulPublish => _keepBranchOnSuccessfulPublish;
}
