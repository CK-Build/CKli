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
    int _width;

    public AnsiScreen( uint? originalConsoleMode )
    {
        _originalConsoleMode = originalConsoleMode;
        _originalOutputEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;
        _target = new RenderTarget( Console.Out.Write );
        _width = ConsoleScreen.GetWindowWidth();
        _animation = new Animation( _target, _width );
    }

    public ScreenType ScreenType => _screenType;

    public void Display( IRenderable renderable ) => Display( renderable, false );

    public void Display( IRenderable renderable, bool newLine )
    {
        _animation.Hide();
        if( renderable.Width > _width )
        {
            renderable = renderable.SetWidth( _width, false );
        }
        renderable.Render( _target, newLine );
    }

    public int Width => _width;

    public void ScreenLog( LogLevel level, string message )
    {
        _animation.Hide();
        _screenType.CreateLog( level, message ).Render( _target );
    }

    public void OnLog( LogLevel level, string? text, bool isOpenGroup )
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

    InteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor, CKliEnv context ) => new Driver( this, context ).InteractiveScreen;

    public override string ToString() => string.Empty;
}
