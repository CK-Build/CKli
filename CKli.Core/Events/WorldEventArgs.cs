using CK.Core;

namespace CKli.Core;

/// <summary>
/// Base class for all events raised by a <see cref="World"/>.
/// <para>
/// This is also used by plugins.
/// </para>
/// </summary>
public abstract class WorldEventArgs : EventMonitoredArgs
{
    readonly CKliEnv _context;
    readonly World _world;

    /// <summary>
    /// Initializes a new world event.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The CKli context.</param>
    /// <param name="world">The current world.</param>
    protected WorldEventArgs( IActivityMonitor monitor, CKliEnv context, World world )
        : base( monitor )
    {
        _context = context;
        _world = world;
    }

    /// <summary>
    /// Gets the CKli minimal context.
    /// </summary>
    public CKliEnv Context => _context;

    /// <summary>
    /// The source World.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets the screen.
    /// </summary>
    public IScreen Screen => _context.Screen;

    /// <summary>
    /// Gets the screen type.
    /// </summary>
    public ScreenType ScreenType => _world.ScreenType;

}
