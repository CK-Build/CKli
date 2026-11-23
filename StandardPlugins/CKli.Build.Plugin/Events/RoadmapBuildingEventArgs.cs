using CK.Core;
using CKli.Core;

namespace CKli.Build.Plugin;


/// <summary>
/// Event raised by <see cref="BuildPlugin"/> when a <see cref="Roadmap"/> is about to be built: it has been computed
/// and displayed, nothing has been built yet. It is not raised by a <see cref="Roadmap.DryRun"/> (nothing is built).
/// <para>
/// This is where a listener refuses a roadmap whose fate is known from the roadmap alone: <see cref="BuildBaseEventArgs.SetFailed()"/>
/// stops the command before any build. A regular build integrates the "dev/" branches into their base branch before
/// building them: a refusal after the builds would leave the base branches moved.
/// </para>
/// </summary>
public sealed class RoadmapBuildingEventArgs : BuildBaseEventArgs
{
    readonly Roadmap _roadmap;
    readonly bool? _runTest;

    internal RoadmapBuildingEventArgs( IActivityMonitor monitor, CKliEnv context, World world, Roadmap roadmap, bool? runTest )
        : base( monitor, context, world, roadmap.MustPublish )
    {
        _roadmap = roadmap;
        _runTest = runTest;
    }

    /// <summary>
    /// Gets the roadmap that is about to be built.
    /// </summary>
    public Roadmap Roadmap => _roadmap;

    /// <summary>
    /// Gets the tests option of the command: null by default (the tests run when they never ran on the content),
    /// false for "--skip-tests" and true for "--force-tests". See <see cref="RoadmapBuildEventArgs.RunTest"/>.
    /// </summary>
    public bool? RunTest => _runTest;
}
