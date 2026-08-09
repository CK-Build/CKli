using CK.Core;
using CKli.ArtifactHandler.Plugin;
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
    readonly bool _isCIBuild;

    internal FixBuildEventArgs( IActivityMonitor monitor,
                                FixWorkflow fix,
                                bool isCIBuild,
                                ImmutableArray<BuildResult> results,
                                bool shouldPublish,
                                bool keepBranchOnSuccessfulPublish )
        : base( monitor, shouldPublish )
    {
        _fix = fix;
        _isCIBuild = isCIBuild;
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
    /// Gets whether the build is a CI build.
    /// </summary>
    public bool IsCIBuild => _isCIBuild;

    /// <summary>
    /// Gets whether the "fix/" branches must be kept once the fix is published.
    /// </summary>
    public bool KeepBranchOnSuccessfulPublish => _keepBranchOnSuccessfulPublish;
}
