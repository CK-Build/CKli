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
    readonly StringBuilder _buffer;

    /// <summary>
    /// Initializes a new string screen.
    /// </summary>
    public StringScreen()
    {
        _buffer = new StringBuilder();
    }

    /// <inheritdoc />
    public ScreenType ScreenType => ScreenType.Default;

    /// <inheritdoc />
    public void Display( IRenderable renderable ) => _buffer.Append( renderable.RenderAsString() );

    /// <inheritdoc />
    public int Width => IScreen.MaxScreenWidth;

    /// <inheritdoc />
    public void OnLog( LogLevel level, string message, bool isOpenGroup )
    {
        _buffer.Append( level == LogLevel.Warn ? "Warning: " : "Error: " ).Append( message ).AppendLine();
    }

    void IScreen.OnLogOther( LogLevel level, string? text, bool isOpenGroup )
    {
    }

    void IScreen.Close()
    {
    }

    IInteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor )
    {
        monitor.Warn( $"Screen type '{nameof( StringScreen )}' doesn't support interactive mode." );
        return null;
    }


    /// <summary>
    /// Gets the screen content.
    /// </summary>
    /// <returns>The screen content.</returns>
    public override string ToString() => _buffer.ToString();

    internal sealed class Renderer : IRenderTarget
    {
        readonly StringBuilder _b;

        public Renderer( StringBuilder b ) => _b = b;

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
