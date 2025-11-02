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
    IRenderable? _errorHead;
    IRenderable? _warningHead;
    int _width;

    IRenderable ErrorHead => _errorHead ??= _screenType.Text( "Error:" )
                                            .Box( paddingLeft: 1,
                                                  paddingRight: 1,
                                                  style: new TextStyle( new Color( ConsoleColor.Black, ConsoleColor.DarkRed ), TextEffect.Bold ) );

    IRenderable WarningHead => _warningHead ??= _screenType.Text( "Warning:" )
                                                .Box( paddingLeft: 1,
                                                      paddingRight: 1,
                                                      style: new TextStyle( new Color( ConsoleColor.Yellow, ConsoleColor.Black ), TextEffect.Bold ) );


    public AnsiScreen( uint? originalConsoleMode )
    {
        _originalConsoleMode = originalConsoleMode;
        _originalOutputEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;
        _target = new RenderTarget( Console.Out.Write );
        _width = GetWindowWidth();
        _animation = new Animation( _target, _width );
    }

    public ScreenType ScreenType => _screenType;

    public void Display( IRenderable renderable ) => Display( renderable, false );

    public void Display( IRenderable renderable, bool newLine )
    {
        _animation.Hide();
        if( renderable.Width > _width )
        {
            renderable = renderable.SetWidth( _width );
        }
        renderable.Render( _target, newLine );
    }

    public int Width => _width;

    static int GetWindowWidth()
    {
        try
        {
            return Console.IsOutputRedirected || Console.BufferWidth == 0 || Console.BufferWidth > IScreen.MaxScreenWidth
                        ? IScreen.MaxScreenWidth
                        : Console.BufferWidth;
        }
        catch
        {
            return IScreen.MaxScreenWidth;
        }
    }

    public void OnLog( LogLevel level, string message, bool isOpenGroup )
    {
        _animation.Hide();
        CreateLog( level, message ).Render( _target );
        if( isOpenGroup ) _animation.OpenGroup( level, message );
        else _animation.Line( level, message );
    }

    IRenderable CreateLog( LogLevel level, string message )
    {
        if( level >= LogLevel.Warn )
        {
            var h = level == LogLevel.Warn ? WarningHead : ErrorHead;
            return h.AddRight( _screenType.Text( message, TextStyle.Default ) );
        }
        return _screenType.Text( message, TextStyle.Default );
    }

    public void OnLogOther( LogLevel level, string? text, bool isOpenGroup )
    {
        if( text == null ) _animation.CloseGroup();
        else if( isOpenGroup ) _animation.OpenGroup( level, text );
        else _animation.Line( level, text );
    }

    void IScreen.Close()
    {
        _animation.Dispose();
        Console.OutputEncoding = _originalOutputEncoding;
        AnsiDetector.RestoreConsoleMode( _originalConsoleMode );
    }

    IInteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor ) => new Interactive( this );

    public override string ToString() => string.Empty;
}
