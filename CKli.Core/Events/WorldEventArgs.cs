using CK.Core;

namespace CKli.Core;

/// <summary>
/// Base class for all events raised by a <see cref="World"/>.
/// </summary>
public abstract class WorldEventArgs : EventMonitoredArgs
{
    readonly World _world;
    bool _success;

    private protected WorldEventArgs( IActivityMonitor monitor, World world )
        : base( monitor )
    {
        _world = world;
    }

    /// <summary>
    /// The source World.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets or sets whether the handle of the event failed.
    /// <para>
    /// When false, event handlers should normally ignore the event.
    /// </para>
    /// </summary>
    public bool Success
    {
        get => _success;
        set => _success = value;
    }
}
