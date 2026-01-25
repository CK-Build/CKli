using CK.Core;

namespace CKli.Core;

/// <summary>
/// A screen handles the console output. This interface cannot be implemented outside CKli.Core:
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
    /// Displays the renderable.
    /// </summary>
    /// <param name="renderable">The renderable to display.</param>
    /// <param name="newLine">False to not append a new line after the renderable.</param>
    /// <param name="fill">True to fill the rest of each line with the current background color.</param>
    void Display( IRenderable renderable, bool newLine = true, bool fill = true );

    /// <summary>
    /// Gets the width of the screen.
    /// Never 0, defaults to <see cref="MaxScreenWidth"/>.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Called whenever a error (can be <see cref="LogLevel.Fatal"/>) or a warning or
    /// <see cref="ScreenType.CKliScreenTag"/> log is emitted.
    /// <para>
    /// Can be used directly to display non logged errors or warnings.
    /// </para>
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="text">The message.</param>
    void ScreenLog( LogLevel level, string text );


    /// <summary>
    /// All screens returns an empty string except <see cref="StringScreen"/> that returns
    /// the screen content.
    /// </summary>
    /// <returns>A non null string.</returns>
    string ToString();

    internal void OnCommandExecuted( bool success, CommandLineArguments cmdLine );

    internal void OnLog( LogLevel level, string? text, bool isOpenGroup );

    internal void Close();

    internal InteractiveScreen? TryCreateInteractive( IActivityMonitor monitor, CKliEnv context );
}
