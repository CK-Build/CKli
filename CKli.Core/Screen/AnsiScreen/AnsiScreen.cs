using CK.Core;
using System;
using System.Text;

namespace CKli.Core;

sealed partial class AnsiScreen : IScreen
{
    static readonly ScreenType _screenType = new ScreenType( true, true );

    readonly RenderTarget _target;
    readonly uint? _originalConsoleMode;
    readonly Encoding _originalOutputEncoding;
    readonly Animation _animation;

    public AnsiScreen( uint? originalConsoleMode )
    {
        _originalConsoleMode = originalConsoleMode;
        _originalOutputEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;
        _target = new RenderTarget( Console.Out.Write );
        _animation = new Animation( _target );
    }

    public ScreenType ScreenType => _screenType;

    public void Display( IRenderable renderable, bool newLine = true, bool fill = true )
    {
        _animation.Hide( false );
        if( renderable.Width > _animation.ScreenWidth )
        {
            renderable = renderable.SetWidth( _animation.ScreenWidth, false );
        }
        renderable.Render( _target, newLine, fill );
        _animation.Show();
    }

    public int Width => _animation.ScreenWidth;

    public void ScreenLog( LogLevel level, string message ) => _animation.AddLog( _screenType.CreateLog( level, message ) );

    public void OnLog( LogLevel level, string? text, bool isOpenGroup ) => _animation.OnLog( text, isOpenGroup );

    void IScreen.OnCommandExecuted( bool success, CommandLineArguments cmdLine )
    {
        _animation.Hide( true );
        ScreenExtensions.DisplayCommandSuccessOrFailure( this, success, cmdLine );
    }

    void IScreen.Close()
    {
        _animation.Dispose();
        // Reset attributes + explicitly set default fg/bg colors + erase to end of screen.
        // SGR 39 = default foreground, SGR 49 = default background.
        // Must be before RestoreConsoleMode which may disable VT processing on Windows.
        Console.Out.Write( "\u001b[0;39;49m\u001b[J" );
        Console.Out.Flush();
        Console.OutputEncoding = _originalOutputEncoding;
        AnsiDetector.RestoreConsoleMode( _originalConsoleMode );
    }

    InteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor, CKliEnv context ) => new Driver( this, context ).InteractiveScreen;

    public override string ToString() => string.Empty;
}
