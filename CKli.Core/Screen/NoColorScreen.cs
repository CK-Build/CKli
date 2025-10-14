using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace CKli.Core;

sealed class NoColorScreen : IScreen
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

        public void EndOfLine() => Append( Environment.NewLine, TextStyle.None );
    }

    readonly RenderTarget _target;
    int? _width;

    public NoColorScreen()
    {
        _target = new RenderTarget();
    }

    public void Clear() => Console.Clear();

    public void Display( IRenderable renderable )
    {
        renderable.Render( _target );
    }

    public int Width => _width ??= GetWindowWidth();

    static int GetWindowWidth()
    {
        try
        {
            // It f*c%$ doesn't work: this seems to return the number of characters
            // when the window is maximized...
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
        var help = ScreenHelpers.CreateDisplayHelp( commands, cmdLine, globalOptions, globalFlags, Width );
        help.Render( _target );
    }

    public void DisplayPluginInfo( string headerText, List<World.DisplayInfoPlugin>? infos )
    {
        var display = ScreenHelpers.CreateDisplayPlugin( headerText, infos, Width );
        display.Render( _target );
    }

    public void OnLogErrorOrWarning( LogLevel level, string message )
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

    public void OnLogAny( LogLevel level, string? text, bool isOpenGroup )
    {
    }

    void IScreen.Close()
    {
    }

    public override string ToString() => string.Empty;

}
