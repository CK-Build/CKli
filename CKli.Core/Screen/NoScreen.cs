using CK.Core;

namespace CKli.Core;

/// <summary>
/// A screen that renders nothing.
/// </summary>
public sealed class NoScreen : IScreen
{
    /// <inheritdoc />
    public ScreenType ScreenType => ScreenType.Default;

    /// <inheritdoc />
    public void Display( IRenderable renderable, bool newLine = true, bool fill = true ) { }

    /// <inheritdoc />
    public int Width => IScreen.MaxScreenWidth;

    /// <inheritdoc />
    public void ScreenLog( LogLevel level, string message ) { }

    void IScreen.OnLog( LogLevel level, string? text, bool isOpenGroup ) { }

    void IScreen.Close() { }

    void IScreen.OnCommandExecuted( bool success, CommandLineArguments cmdLine )
    {
        ScreenExtensions.DisplayCommandSuccessOrFailure( this, success, cmdLine );
    }

    InteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor, CKliEnv context )
    {
        monitor.Warn( $"Screen type '{nameof( NoScreen )}' doesn't support interactive mode." );
        return null;
    }

    /// <summary>
    /// Returns an empty string.
    /// </summary>
    /// <returns>An empty string.</returns>
    public override string ToString() => string.Empty;


}
