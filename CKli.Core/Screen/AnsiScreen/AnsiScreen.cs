using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace CKli.Core;

sealed partial class AnsiScreen : IScreen
{
    static readonly IRenderable _warningHead = TextBlock.FromText( "Warning:" )
                                                        .Box( paddingLeft: 1,
                                                              paddingRight: 1,
                                                              style: new TextStyle( new Color( ConsoleColor.Yellow, ConsoleColor.Black ), TextEffect.Bold ) );

    static readonly IRenderable _errorHead = TextBlock.FromText( "Error:" )
                                                        .Box( paddingLeft: 1,
                                                              paddingRight: 1,
                                                              style: new TextStyle( new Color( ConsoleColor.Black, ConsoleColor.DarkRed ), TextEffect.Bold ) );

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

        _target = new RenderTarget( Console.Out );
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
    }

    void HideSpin()
    {
    }

    void IScreen.Close()
    {
        HideSpin();
        Console.OutputEncoding = _originalOutputEncoding;
        AnsiDetector.RestoreConsoleMode( _originalConsoleMode );
    }

    public override string ToString() => string.Empty;

}
