using CK.Core;
using System;
using System.Text;

namespace CKli.Core;

sealed class NoColorScreen : IScreen
{
    static ScreenType _screenType = new ScreenType( true, false );

    readonly RenderTarget _target;
    int? _width;

    public NoColorScreen()
    {
        _target = new RenderTarget();
    }

    public ScreenType ScreenType => _screenType;

    public void Display( IRenderable renderable )
    {
        if( renderable.Width > _width )
        {
            renderable = renderable.SetWidth( Width );
        }
        renderable.Render( _target );
    }

    public int Width => _width ??= GetWindowWidth();

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
        if( level == LogLevel.Warn )
        {
            Console.Write( "Warning: " );
        }
        else
        {
            Console.Write( "Error: " );
        }
        var b = new StringBuilder();
        b.AppendMultiLine( "         ", message, prefixOnFirstLine: false );
        Console.Out.WriteLine( b.ToString() );
    }

    public void OnLogOther( LogLevel level, string? text, bool isOpenGroup )
    {
    }

    void IScreen.Close()
    {
    }

    public IInteractiveScreen? TryCreateInteractive( IActivityMonitor monitor )
    {
        monitor.Warn( $"Screen type '{nameof(NoColorScreen)}' doesn't support interactive mode yet." );
        return null;
    }

    public override string ToString() => string.Empty;

    sealed class RenderTarget : IRenderTarget
    {
        StringBuilder _buffer = new StringBuilder();
        int _updateCount;

        public void BeginUpdate() => _updateCount++;

        public void EndUpdate()
        {
            if( --_updateCount == 0 )
            {
                Console.Out.Write( _buffer.ToString() );
                _buffer.Clear();
            }
        }

        public void Write( ReadOnlySpan<char> text, TextStyle style )
        {
            if( _updateCount != 0 )
            {
                _buffer.Append( text );
            }
            else
            {
                Console.Out.Write( text );
            }
        }

        public ScreenType ScreenType => _screenType;

        public void EndOfLine( bool newLine )
        {
            if( newLine )
            {
                Write( Environment.NewLine, TextStyle.None );
            }
        }
    }

}
