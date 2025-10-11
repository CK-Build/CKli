using CK.Core;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// A screen that renders nothing.
/// </summary>
public sealed class NoScreen : IScreen
{
    public void Clear() {}

    public void Display( IRenderable renderable ) { }

    public int Width => IScreen.MaxScreenWidth;

    public void OnLogErrorOrWarning( LogLevel level, string message ) { }

    void IScreen.OnLogAny( LogLevel level, string? text, bool isOpenGroup ) { }

    public void DisplayHelp( List<CommandHelp> commands, CommandLineArguments cmdLine, ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions = default, ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags = default )
    {
    }

    void IScreen.DisplayPluginInfo( string headerText, List<World.DisplayInfoPlugin>? infos )
    {
    }

    void IScreen.Close()
    {
    }

    public override string ToString() => string.Empty;

}
