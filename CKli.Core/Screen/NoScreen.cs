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

    public void OnLogErrorOrWarning( LogLevel level, string message, bool isOpenGroup ) { }

    void IScreen.OnLogOther( LogLevel level, string? text, bool isOpenGroup ) { }

    void IScreen.Close()
    {
    }

    IInteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor )
    {
        monitor.Warn( $"Screen type '{nameof( NoScreen )}' doesn't support interactive mode." );
        return null;
    }

    public override string ToString() => string.Empty;


}
