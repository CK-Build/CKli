using CK.Core;

namespace CKli.Core;

/// <summary>
/// A screen that renders nothing.
/// </summary>
public sealed class NoScreen : IScreen
{
    public NoScreen()
    {
    }

    public ScreenType ScreenType => ScreenType.Default;

    public void Display( IRenderable renderable ) { }

    public int Width => IScreen.MaxScreenWidth;

    public void ScreenLog( LogLevel level, string message ) { }

    void IScreen.OnLog( LogLevel level, string? text, bool isOpenGroup ) { }

    void IScreen.Close() { }

    InteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor, CKliEnv context )
    {
        monitor.Warn( $"Screen type '{nameof( NoScreen )}' doesn't support interactive mode." );
        return null;
    }

    public override string ToString() => string.Empty;


}
