using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace CKli.Core;

sealed class AnsiScreen : IScreen
{
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

        public void Append( ReadOnlySpan<char> text, TextStyle style )
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

        public void EndOfLine() => Append( Environment.NewLine, TextStyle.Default );
    }

    static readonly char[] _spinChars = ['|', '/', '-', '\\'];
    static readonly IRenderable _warningHead = TextBlock.FromText( "Warning:", new TextStyle( new Color( ConsoleColor.Yellow, ConsoleColor.Black ), TextEffect.Bold ) )
                                                        .Box( paddingLeft: 1, paddingRight: 1 );
    static readonly IRenderable _errorHead = TextBlock.FromText( "Error:", new TextStyle( new Color( ConsoleColor.Black, ConsoleColor.Red ), TextEffect.Bold ) )
                                                        .Box( paddingLeft: 1, paddingRight: 1 );

    readonly RenderTarget _target;
    readonly uint? _originalConsoleMode;
    readonly Encoding _originalOutputEncoding;
    long _prevTick;
    int? _width;
    int _spinCount;
    bool _hasSpin;

    public AnsiScreen( uint? originalConsoleMode )
    {
        _originalConsoleMode = originalConsoleMode;
        _originalOutputEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;

        _target = new RenderTarget();
    }

    public void Clear() => Console.Clear();

    public void Display( IRenderable renderable )
    {
        HideSpin();
        renderable.Render( _target );
    }

    public int Width => _width ??= GetWindowWidth();

    static int GetWindowWidth()
    {
        try
        {
            // This f*c%$ doesn't work: this seems to return the number of characters when the window is maximized...
            return Console.IsOutputRedirected || Console.BufferWidth == 0
                        ? IScreen.MaxScreenWidth
                        : Console.BufferWidth;
        }
        catch
        {
            return IScreen.MaxScreenWidth;
        }
    }

    public void DisplayHelp( List<CommandHelp> commands,
                             CommandLineArguments cmdLine,
                             ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions = default,
                             ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags = default )
    {
        HideSpin();
        var help = ScreenHelpers.CreateDisplayHelp( commands, cmdLine, globalOptions, globalFlags, Width );
        help.Render( _target );
    }

    public void DisplayPluginInfo( string headerText, List<World.DisplayInfoPlugin>? infos )
    {
        HideSpin();
        var display = ScreenHelpers.CreateDisplayPlugin( headerText, infos, Width );
        display.Render( _target );
    }

    public void OnLogErrorOrWarning( LogLevel level, string message )
    {
        HideSpin();
        var h = level == LogLevel.Warn ? _warningHead : _errorHead;
        h.AddRight( TextBlock.FromText( message, TextStyle.Default ) ).Render( _target );
    }

    public void OnLogAny( LogLevel level, string? text, bool isOpenGroup )
    {
        var now = Environment.TickCount64;
        if( now - _prevTick > 150 )
        {

            if( _hasSpin ) Console.Write( '\b' );
            Console.Out.Write( NextSpin() );
            _prevTick = now;
        }
        _hasSpin = true;
    }

    char NextSpin()
    {
        if( ++_spinCount == _spinChars.Length ) _spinCount = 0;
        return _spinChars[_spinCount];
    }

    void HideSpin()
    {
        if( _hasSpin )
        {
            Console.Write( '\b' );
            _hasSpin = false;
        }
    }

    void IScreen.Close()
    {
        if( _hasSpin ) Console.Write( "\b " );
        Console.OutputEncoding = _originalOutputEncoding;
        AnsiDetector.RestoreConsoleMode( _originalConsoleMode );
    }

    public override string ToString() => string.Empty;

}
