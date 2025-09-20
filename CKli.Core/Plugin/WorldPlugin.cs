using System;

namespace CKli.Core;

/// <summary>
/// Non generic base for <see cref="RepoPlugin{T}"/>.
/// </summary>
public abstract class WorldPlugin
{
    readonly World _world;

    protected WorldPlugin( World world )
    {
        _world = world;
    }

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World => _world;
}
