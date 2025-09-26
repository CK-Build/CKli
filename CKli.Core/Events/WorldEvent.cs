using CK.Core;

namespace CKli.Core;

/// <summary>
/// Base class for all events raised by a <see cref="World"/>.
/// </summary>
public abstract class WorldEvent : EventMonitoredArgs
{
    readonly World _world;
    bool _success;

    private protected WorldEvent( IActivityMonitor monitor, World world )
        : base( monitor )
    {
        _world = world;
        _success = true;
    }

    /// <summary>
    /// The source World.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets or sets whether the handle of the event failed.
    /// Defaults to true.
    /// <para>
    /// When set to false by previous handlers, event handlers should normally ignore the event.
    /// </para>
    /// </summary>
    public bool Success
    {
        get => _success;
        set => _success = value;
    }
}
