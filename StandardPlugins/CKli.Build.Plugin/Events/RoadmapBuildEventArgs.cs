using CK.Core;
using CKli.Core;

namespace CKli.Build.Plugin;


/// <summary>
/// Event raised by <see cref="BuildPlugin"/> when a <see cref="Roadmap"/> has been successfully
/// built (or <see cref="Roadmap.DryRun"/> is true).
/// </summary>
public sealed class RoadmapBuildEventArgs : BuildBaseEventArgs
{
    readonly Roadmap _roadmap;
    readonly int _maxDop;
    readonly bool? _runTest;

    internal RoadmapBuildEventArgs( IActivityMonitor monitor, CKliEnv context, World world, Roadmap roadmap, int maxDop, bool? runTest )
        : base( monitor, context, world, roadmap.MustPublish )
    {
        _roadmap = roadmap;
        _maxDop = maxDop;
        _runTest = runTest;
    }

    /// <summary>
    /// Gets the roadmap that has been successfully build (or <see cref="Roadmap.DryRun"/> is true).
    /// </summary>
    public Roadmap Roadmap => _roadmap;

    /// <summary>
    /// Gets the "--max-dop" of the command: the maximal number of solutions that are built concurrently, and
    /// the bound that applies to the listeners that parallelize their own work on the roadmap (the publication).
    /// </summary>
    public int MaxDop => _maxDop;

    /// <summary>
    /// Gets the tests option of the command: null by default (the tests run when they never ran on the content),
    /// false for "--skip-tests" and true for "--force-tests".
    /// <para>
    /// This applies to the builds and to the publication. A publication is never "--skip-tests" (this is never false):
    /// a release that is published but has not been built by this roadmap must have been tested
    /// (see <see cref="BuildPlugin.EnsureTestedAsync"/>).
    /// </para>
    /// </summary>
    public bool? RunTest => _runTest;
}

