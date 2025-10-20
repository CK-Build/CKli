using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// A screen handles the console output. This interface cannot be implemented outside CKLi.Core:
/// the only concrete screens that exist are internal except <see cref="StringScreen"/> and <see cref="NoScreen"/>.
/// </summary>
public interface IScreen
{
    /// <summary>
    /// Default screen width. Applies when no screen width can be obtained.
    /// </summary>
    public const int MaxScreenWidth = short.MaxValue;

    /// <summary>
    /// Gets the screen type.
    /// </summary>
    ScreenType ScreenType { get; }

    /// <summary>
    /// Clears the screen.
    /// </summary>
    void Clear();

    /// <summary>
    /// Displays the renderable.
    /// When possible, <see cref="Width"/> should be considered.
    /// </summary>
    /// <param name="renderable">The renderable to display.</param>
    void Display( IRenderable renderable );

    /// <summary>
    /// Gets the width of the screen.
    /// Never 0, defaults to <see cref="MaxScreenWidth"/>.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Called whenever a error (can be <see cref="LogLevel.Fatal"/>) or a warning is logged.
    /// <para>
    /// Can be used directly to emit non logged errors or warnings.
    /// </para>
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="text">The message.</param>
    void OnLogErrorOrWarning( LogLevel level, string text, bool isOpenGroup = false );

    /// <summary>
    /// All screens returns an empty string except <see cref="StringScreen"/> that returns
    /// the screen content.
    /// </summary>
    /// <returns>A non null string.</returns>
    string ToString();

    internal void OnLogOther( LogLevel level, string? text, bool isOpenGroup );

    internal void DisplayHelp( List<CommandHelp> commands,
                               CommandLineArguments cmdLine,
                               ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> globalOptions = default,
                               ImmutableArray<(ImmutableArray<string> Names, string Description)> globalFlags = default );


    internal void DisplayPluginInfo( string headerText, List<World.DisplayInfoPlugin>? infos );

    internal void Close();
}
