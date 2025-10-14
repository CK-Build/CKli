using System;
using System.Runtime.InteropServices;

namespace CKli.Core;

public enum CursorRelativeSpan
{
    After,
    Before,
    Both
}

/// <summary>
/// A collection of standard ANSI/VT100 control codes.
/// </summary>
internal static class AnsiCodes
{
    /// <summary>
    /// Progress state (busy spinner) is not available on all platforms.
    /// <para>
    /// <see cref="SetProgressIndicator"/> and <see cref="RemoveProgressIndicator"/> code works only on ConEmu terminals, and conflicts
    /// with push a notification code on iTerm2.
    /// </para>
    /// <see href="https://conemu.github.io/en/AnsiEscapeCodes.html#ConEmu_specific_OSC">ConEmu specific OSC codes.</see><br/>
    /// <see href="https://iterm2.com/documentation-escape-codes.html">iTerm2 proprietary escape codes.</see>
    /// </summary>
    static bool _supportsProgressReporting = !RuntimeInformation.IsOSPlatform( OSPlatform.OSX );

    /// <summary>
    /// Clears the screen.
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string EraseScreen( CursorRelativeSpan span = CursorRelativeSpan.Both ) => $"\u001b[{(int)span}J";

    /// <summary>
    /// Clears the current line.
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string EraseLine( CursorRelativeSpan span = CursorRelativeSpan.Both ) => $"\u001b[{(int)span}K";

    /// <summary>
    /// Moves the cursor at the given (1-based) position.
    /// </summary>
    /// <param name="line">The 1-based line.</param>
    /// <param name="column">The 1-based column.</param>
    /// <returns>The Ansi string.</returns>
    public static string MoveTo( int line, int column ) => $"\u001b[{line};{column}H";

    /// <summary>
    /// Moves the cursor up.
    /// </summary>
    /// <param name="lineCount">Number of lines.</param>
    /// <returns>The Ansi string.</returns>
    public static string MoveUp( int lineCount = 1 ) => $"\u001b[{lineCount}A";

    /// <summary>
    /// Moves the cursor down.
    /// </summary>
    /// <param name="lineCount">Number of lines.</param>
    /// <returns>The Ansi string.</returns>
    public static string MoveDown( int lineCount = 1 ) => $"\u001b[{lineCount}B";

    /// <summary>
    /// Moves the cursor forward.
    /// </summary>
    /// <param name="columnCount">Number of columns.</param>
    /// <returns>The Ansi string.</returns>
    public static string MoveForward( int columnCount = 1 ) => $"\u001b[{columnCount}C";

    /// <summary>
    /// Moves the cursor backward.
    /// </summary>
    /// <param name="columnCount">Number of columns.</param>
    /// <returns>The Ansi string.</returns>
    public static string MoveBackward( int columnCount = 1 ) => $"\u001b[{columnCount}D";

    /// <summary>
    /// Moves the cursor to beginning of the next line.
    /// </summary>
    /// <param name="lineCount">Number of lines.</param>
    /// <returns>The Ansi string.</returns>
    public static string MoveToNextLine( int lineCount = 1 ) => $"\u001b[{lineCount}E";

    /// <summary>
    /// Moves the cursor to beginning of the previous line.
    /// </summary>
    /// <param name="lineCount">Number of lines.</param>
    /// <returns>The Ansi string.</returns>
    public static string MoveToPrevLine( int lineCount = 1 ) => $"\u001b[{lineCount}F";

    /// <summary>
    /// Moves the cursor to the given 1-based column on the current line, or the rightmost column if <paramref name="column"/>
    /// is greater than the width of the terminal.
    /// </summary>
    /// <param name="column">1-based column number.</param>
    /// <returns>The Ansi string.</returns>
    public static string MoveToColumn( int column = 1 ) => $"\u001b[{column}G";

    /// <summary>
    /// Show or hides the cursor.
    /// </summary>
    /// <param name="show">False to hide the cursor.</param>
    /// <returns>The Ansi string.</returns>
    public static string ShowCursor( bool show = true ) => show ? "\x1b[?25h" : "\x1b[?25l";

    /// <summary>
    /// Saves the cursor position. Can be restored by <see cref=""/>
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string SaveCursorPosition() => "\x1b[s";

    /// <summary>
    /// Saves the cursor position. Can be restored by <see cref=""/>
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string RestoreCursorPosition() => "\x1b[u";

    /// <summary>
    /// Renders an hyperlink.
    /// </summary>
    /// <param name="text">The link text.</param>
    /// <param name="url">The target url.</param>
    /// <returns>The Ansi string.</returns>
    public static string Hyperlink( string text, string url ) => "\u001b]8;;{url}\x1b\\{text}\u001b]8;;\u001b\\";

    /// <summary>
    /// Renders an hyperlink.
    /// </summary>
    /// <param name="url">The target url.</param>
    /// <returns>The Ansi string.</returns>
    public static string Hyperlink( string url ) => Hyperlink( url, url );

    /// <summary>
    /// Sets the foreground color.
    /// </summary>
    /// <param name="color">The color to set.</param>
    /// <returns>The Ansi string.</returns>
    public static string SetForeColor( ConsoleColor color ) => $"\x1b[{(int)color.FromConsole()}m";

    /// <summary>
    /// Sets the background color.
    /// </summary>
    /// <param name="color">The color to set.</param>
    /// <returns>The Ansi string.</returns>
    public static string SetBackColor( ConsoleColor color ) => $"\x1b[{10 + (int)color.FromConsole()}m";

    /// <summary>
    /// Resets the colors to the default foreground and background colors. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string ResetColors() => "\u001b[39m\u001b[49m";

    /// <summary>
    /// Sets or clears bold mode. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string SetBold( bool bold = true ) => bold ? "\u001b[1m" : "\u001b[21m";

    /// <summary>
    /// Sets or clears underlined mode. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string SetUnderline( bool underline = true ) => underline ? "\u001b[4m" : "\u001b[24m";

    /// <summary>
    /// Set progress state to a busy spinner (does nothing by returning an empty string if this
    /// is not supported on the current platform).
    /// </summary>
    public static string SetProgressIndicator() => _supportsProgressReporting ? "\x1b]9;4;3;\x1b\\" : "";

    /// <summary>
    /// Remove progress state, restoring taskbar status to normal (does nothing by returning an empty string if this
    /// is not supported on the current platform).
    /// </summary>
    public static string RemoveProgressIndicator() => _supportsProgressReporting ? "\x1b]9;4;0;\x1b\\" : "";

}
