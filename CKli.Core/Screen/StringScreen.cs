using CK.Core;
using System;
using System.Text;

namespace CKli.Core;

/// <summary>
/// A screen that renders in a StringBuilder.
/// Use <see cref="ToString()"/> to retrieve the screen content.
/// </summary>
public sealed class StringScreen : IScreen
{
    readonly StringBuilder _b;
    readonly RenderTarget _renderer;

    /// <summary>
    /// Initializes a new string screen.
    /// </summary>
    public StringScreen()
    {
        _b = new StringBuilder();
        _renderer = new RenderTarget( _b );
    }

    /// <inheritdoc />
    public ScreenType ScreenType => ScreenType.Default;

    /// <inheritdoc />
    public void Display( IRenderable renderable, bool newLine = true ) => renderable.Render( _renderer, newLine );

    /// <inheritdoc />
    public int Width => IScreen.MaxScreenWidth;

    /// <inheritdoc />
    public void ScreenLog( LogLevel level, string message ) => ScreenType.Default.CreateLog( level, message ).Render( _renderer );

    void IScreen.OnLog( LogLevel level, string? text, bool isOpenGroup ) { }

    void IScreen.Close() { }

    void IScreen.OnCommandExecuted( bool success, CommandLineArguments cmdLine )
    {
        ScreenExtensions.DisplayCommandSuccessOrFailure( this, success, cmdLine );
    }

    InteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor, CKliEnv context )
    {
        monitor.Warn( $"Screen type '{nameof( StringScreen )}' doesn't support interactive mode." );
        return null;
    }

    /// <summary>
    /// Clears the screen content.
    /// </summary>
    public void Clear() => _b.Clear();

    /// <summary>
    /// Gets the screen content.
    /// </summary>
    /// <returns>The screen content.</returns>
    public override string ToString() => _b.ToString();

    internal sealed class RenderTarget : IRenderTarget
    {
        readonly StringBuilder _b;

        public RenderTarget( StringBuilder b ) => _b = b;

        public void Write( ReadOnlySpan<char> s, TextStyle style ) => _b.Append( s );

        public void EndOfLine( bool newLine )
        {
            if( newLine )
            {
                _b.AppendLine();
            }
        }

        public void BeginUpdate() { }

        public void EndUpdate() { }

        public ScreenType ScreenType => ScreenType.Default;

        public override string ToString() => _b.ToString();
    }

}
