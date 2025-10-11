using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace CKli.Core;

sealed class ConsoleScreen : IScreen
{
    static readonly char[] _spinChars = ['|', '/', '-', '\\'];

    long _prevTick;
    int? _width;
    int _spinCount;
    bool _hasSpin;

    public void Clear() => Console.Clear();

    public void Display( IRenderable renderable )
    {
        HideSpin();
        Console.Write( renderable.RenderAsString() );
    }

    public int Width => _width ??= Console.IsOutputRedirected || Console.BufferWidth == 0
                                    ? IScreen.MaxScreenWidth
                                    : Console.BufferWidth;

    public void DisplayHelp( List<CommandHelp> commands,
                             CommandLineArguments cmdLine,
                             ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions = default,
                             ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags = default )
    {
        HideSpin();
        var help = ScreenHelpers.CreateDisplayHelp( commands, cmdLine, globalOptions, globalFlags, Width );
        Console.Write( help.RenderAsString() );
    }

    public void DisplayPluginInfo( string headerText, List<World.DisplayInfoPlugin>? infos )
    {
        HideSpin();
        var display = ScreenHelpers.CreateDisplayPlugin( headerText, infos, IScreen.MaxScreenWidth );
        Console.Write( display.RenderAsString() );
    }

    public void OnLogErrorOrWarning( LogLevel level, string message )
    {
        HideSpin();
        if( level == LogLevel.Warn )
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write( "Error:   " );
        }
        else
        {
            Console.BackgroundColor = ConsoleColor.Red;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write( "Warning: " );
        }
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        WriteMessage( message );
    }

    static void WriteMessage( string message )
    {
        var b = new StringBuilder();
        b.AppendMultiLine( "         ", message, prefixOnFirstLine: false );
        Console.WriteLine( b.ToString() );
    }

    public void OnLogAny( LogLevel level, string? text, bool isOpenGroup )
    {
        var now = Environment.TickCount64;
        if( now - _prevTick > 250 )
        {

            if( _hasSpin ) Console.Write( '\b' );
            Console.Write( NextSpin() );
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

    public void Dispose()
    {
        if( _hasSpin ) Console.Write( "\b " );
    }

    public override string ToString() => string.Empty;

}
