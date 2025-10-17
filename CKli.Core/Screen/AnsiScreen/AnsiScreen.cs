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
    readonly Animation _animation;
    int _width;

    public AnsiScreen( uint? originalConsoleMode )
    {
        _originalConsoleMode = originalConsoleMode;
        _originalOutputEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;

        _target = new RenderTarget( Console.Out.Write );

        _width = GetWindowWidth();
        _animation = new Animation( _target, _width );
    }

    public void Clear() => Console.Clear();

    public void Display( IRenderable renderable )
    {
        _animation.Hide();
        renderable.Render( _target );
    }

    public int Width => _width;

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
        _animation.Hide();
        var help = ScreenHelpers.CreateDisplayHelp( commands, cmdLine, globalOptions, globalFlags, Width );
        help.Render( _target );
    }

    public void DisplayPluginInfo( string headerText, List<World.DisplayInfoPlugin>? infos )
    {
        _animation.Hide();
        var display = ScreenHelpers.CreateDisplayPlugin( headerText, infos, Width );
        display.Render( _target );
    }

    public void OnLogErrorOrWarning( LogLevel level, string message, bool isOpenGroup )
    {
        _animation.Hide();
        var h = level == LogLevel.Warn ? _warningHead : _errorHead;
        h.AddRight( TextBlock.FromText( message, TextStyle.Default ) ).Render( _target );
        if( isOpenGroup ) _animation.OpenGroup( level, message );
        else _animation.Line( level, message );
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

    public override string ToString() => string.Empty;

}
