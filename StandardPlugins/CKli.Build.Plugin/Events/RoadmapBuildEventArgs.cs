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

    internal RoadmapBuildEventArgs( IActivityMonitor monitor, CKliEnv context, World world, Roadmap roadmap, int maxDop )
        : base( monitor, context, world, roadmap.MustPublish )
    {
        _roadmap = roadmap;
        _maxDop = maxDop;
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
}

