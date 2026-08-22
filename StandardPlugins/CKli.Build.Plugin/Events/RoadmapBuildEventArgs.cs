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
    
    internal RoadmapBuildEventArgs( IActivityMonitor monitor, CKliEnv context, World world, Roadmap roadmap )
        : base( monitor, context, world, roadmap.MustPublish )
    {
        _roadmap = roadmap;
    }

    /// <summary>
    /// Gets the roadmap that has been successfully build (or <see cref="Roadmap.DryRun"/> is true).
    /// </summary>
    public Roadmap Roadmap => _roadmap;
}

