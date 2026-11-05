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
    readonly IRenderTarget _renderer;
    int _width = IScreen.MaxScreenWidth;

    /// <summary>
    /// Initializes a new string screen.
    /// </summary>
    /// <param name="useDebugRenderer">
    /// True to use the <see cref="DebugRenderer"/> instead of the text-only default renderer.
    /// </param>
    public StringScreen( bool useDebugRenderer = false )
    {
        _b = new StringBuilder();
        _renderer = useDebugRenderer
                        ? new DebugRenderer( _b )
                        : new RenderTarget( _b );
    }

    /// <inheritdoc />
    public ScreenType ScreenType => ScreenType.Default;

    /// <summary>
    /// Renders the <paramref name="renderable"/>. A renderable wider than <see cref="Width"/> is narrowed first, as
    /// the console screen does.
    /// </summary>
    /// <param name="renderable">The renderable to display.</param>
    /// <param name="newLine">False to not end the last line.</param>
    public void Display( IRenderable renderable, bool newLine = true )
    {
        if( renderable.Width > Width )
        {
            renderable = renderable.SetWidth( Width, false );
        }
        renderable.Render( _renderer, newLine );
    }

    /// <summary>
    /// Gets or sets the width of this screen. Defaults to <see cref="IScreen.MaxScreenWidth"/>: nothing is narrowed.
    /// A test sets a console width (80 for instance) to see what a display becomes on a narrow screen.
    /// </summary>
    public int Width
    {
        get => _width;
        set
        {
            Throw.CheckOutOfRangeArgument( value > 0 && value <= IScreen.MaxScreenWidth );
            _width = value;
        }
    }

    /// <inheritdoc />
    public void ScreenLog( LogLevel level, string message ) => ScreenType.Default.CreateLog( level, message ).Render( _renderer );

    void IScreen.OnLog( LogLevel level, string? text, bool isOpenGroup ) { }

    void IScreen.OnParallelText( string text ) { }

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
