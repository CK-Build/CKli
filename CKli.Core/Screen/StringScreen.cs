using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace CKli.Core;

/// <summary>
/// A screen that renders in a StringBuilder.
/// Use <see cref="ToString()"/> to retrieve the screen content.
/// </summary>
public sealed class StringScreen : IScreen
{
    readonly StringBuilder _buffer;

    public StringScreen()
    {
        _buffer = new StringBuilder();
    }

    public void Clear() => _buffer.Clear();

    public void Display( IRenderable renderable ) => _buffer.Append( renderable.RenderAsString() );

    public int Width => IScreen.MaxScreenWidth;

    public void OnLogErrorOrWarning( LogLevel level, string message )
    {
        _buffer.Append( level == LogLevel.Warn ? "Warning: " : "Error : " ).Append( message ).AppendLine();
    }

    void IScreen.OnLogAny( LogLevel level, string? text, bool isOpenGroup )
    {
    }

    public void DisplayHelp( List<CommandHelp> commands, CommandLineArguments cmdLine, ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions = default, ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags = default )
    {
        var help = ScreenHelpers.CreateDisplayHelp( commands, cmdLine, globalOptions, globalFlags, IScreen.MaxScreenWidth );
        help.RenderAsString( _buffer );
    }

    void IScreen.DisplayPluginInfo( string headerText, List<World.DisplayInfoPlugin>? infos )
    {
        var display = ScreenHelpers.CreateDisplayPlugin( headerText, infos, IScreen.MaxScreenWidth );
        display.RenderAsString( _buffer );
    }

    void IScreen.Close()
    {
    }

    /// <summary>
    /// Gets the screen content.
    /// </summary>
    /// <returns>The screen content.</returns>
    public override string ToString() => _buffer.ToString();

    internal sealed class Renderer : IRenderTarget
    {
        readonly StringBuilder _b;

        public Renderer( StringBuilder b )
        {
            _b = b;
        }

        public void Append( ReadOnlySpan<char> s, TextStyle style ) => _b.Append( s );

        public void EndOfLine() => _b.AppendLine();

        public override string ToString() => _b.ToString();
    }
}
