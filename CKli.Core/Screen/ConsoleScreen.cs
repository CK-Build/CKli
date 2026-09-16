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
        // Same 3 levels as ScreenType.CreateLog (used by the Ansi and String screens): a Info tagged with
        // ScreenType.CKliScreenTag reaches here and must not be labeled as an error.
        var head = level switch
        {
            > LogLevel.Warn => "Error: ",
            LogLevel.Warn => "Warning: ",
            _ => "Info: "
        };
        Console.Write( head );
        var b = new StringBuilder();
        b.AppendMultiLine( new string( ' ', head.Length ), message, prefixOnFirstLine: false );
        Console.Out.WriteLine( b.ToString() );
    }

    public void OnLog( LogLevel level, string? text, bool isOpenGroup )
    {
    }

    public void OnParallelText( string text )
    {
    }

    void IScreen.OnCommandExecuted( bool success, CommandLineArguments cmdLine )
    {
        ScreenExtensions.DisplayCommandSuccessOrFailure( this, success, cmdLine );
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
