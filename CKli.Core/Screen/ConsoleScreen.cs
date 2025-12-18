using CK.Core;
using System;
using System.Text;

namespace CKli.Core;

sealed class ConsoleScreen : IScreen
{
    static ScreenType _screenType = new ScreenType( true, false );

    readonly RenderTarget _target;
    int? _width;

    public ConsoleScreen()
    {
        _target = new RenderTarget();
    }

    public ScreenType ScreenType => _screenType;

    public void Display( IRenderable renderable, bool newLine = true )
    {
        if( renderable.Width > _width )
        {
            renderable = renderable.SetWidth( Width, false );
        }
        renderable.Render( _target, newLine );
    }

    public int Width => _width ??= GetWindowWidth();

    internal static int GetWindowWidth()
    {
        try
        {
            if( Console.IsOutputRedirected ) return IScreen.MaxScreenWidth;
            int w = Console.BufferWidth;
            return w == 0 || w > IScreen.MaxScreenWidth
                        ? IScreen.MaxScreenWidth
                        : w;
        }
        catch
        {
            return IScreen.MaxScreenWidth;
        }
    }

    public void ScreenLog( LogLevel level, string message )
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

    public void OnLog( LogLevel level, string? text, bool isOpenGroup )
    {
    }

    void IScreen.Close() { }

    public InteractiveScreen? TryCreateInteractive( IActivityMonitor monitor, CKliEnv context )
    {
        monitor.Warn( $"Screen type '{nameof(ConsoleScreen)}' doesn't support interactive mode yet." );
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
