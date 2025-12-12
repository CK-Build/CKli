using CK.Core;
using System;
using System.Runtime.InteropServices;

namespace CKli.Core;

/// <summary>
/// A collection of standard ANSI/VT100 control codes.
/// </summary>
internal static class AnsiCodes
{
    public const string HyperLinkPrefix = "\u001b]8;;";
    public const string HyperLinkInfix = "\u001b\\";
    public const string HyperLinkSuffix = "\u001b]8;;\u001b\\";

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
    /// Sets the foreground color.
    /// </summary>
    /// <param name="color">The color to set.</param>
    /// <returns>The Ansi string.</returns>
    public static string SetForeColor( ConsoleColor color ) => $"\u001b[{(int)color.FromConsole()}m";

    /// <summary>
    /// Sets the background color.
    /// </summary>
    /// <param name="color">The color to set.</param>
    /// <returns>The Ansi string.</returns>
    public static string SetBackColor( ConsoleColor color ) => $"\u001b[{10 + (int)color.FromConsole()}m";

    /// <summary>
    /// Sets the colors.
    /// </summary>
    /// <param name="color">The colors to set.</param>
    /// <returns>The Ansi string.</returns>
    public static string SetColor( Color color ) => $"\u001b[{(int)color.ForeColor.FromConsole()},{10 + (int)color.BackColor.FromConsole()}m";

    /// <summary>
    /// Resets the colors to the default foreground and background colors. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string ResetColors() => "\u001b[39,49m";

    /// <summary>
    /// Sets or clears bold mode. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string SetBold( bool bold = true ) => bold ? "\u001b[1m" : "\u001b[22m";

    /// <summary>
    /// Sets or clears underlined mode. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string SetUnderline( bool underline = true ) => underline ? "\u001b[4m" : "\u001b[24m";

    /// <summary>
    /// Sets or clears strikethrough mode. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string SetStrikethrough( bool underline = true ) => underline ? "\u001b[9m" : "\u001b[29m";

    /// <summary>
    /// Sets or clears italic mode. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string SetItalic( bool underline = true ) => underline ? "\u001b[3m" : "\u001b[23m";

    /// <summary>
    /// Sets or clears blink mode. 
    /// </summary>
    /// <returns>The Ansi string.</returns>
    public static string SetBlink( bool underline = true ) => underline ? "\u001b[3m" : "\u001b[23m";

    /// <summary>
    /// Set progress state to a busy spinner (does nothing by returning an empty string if this
    /// is not supported on the current platform).
    /// </summary>
    public static string SetProgressIndicator() => _supportsProgressReporting ? "\u001b]9;4;3;\u001b\\" : "";

    /// <summary>
    /// Remove progress state, restoring taskbar status to normal (does nothing by returning an empty string if this
    /// is not supported on the current platform).
    /// </summary>
    public static string RemoveProgressIndicator() => _supportsProgressReporting ? "\u001b]9;4;0;\u001b\\" : "";

    /// <summary>
    /// Writes the Control Sequence Introducer: '\u001b' and '['.
    /// </summary>
    /// <param name="w">This writer.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool AppendCSI( ref this FixedBufferWriter w ) => w.Append( '\u001b', '[' );

    /// <summary>
    /// Show or hides the cursor.
    /// </summary>
    /// <param name="w">This buffer writer.</param>
    /// <param name="show">False to hide the cursor.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool ShowCursor( ref this FixedBufferWriter w, bool show = true ) => w.Append( show ? "\u001b[?25h" : "\u001b[?25l" );

    /// <summary>
    /// Writes an hyperlink.
    /// </summary>
    /// <param name="w">This buffer writer.</param>
    /// <param name="text">The link text.</param>
    /// <param name="url">The target url.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool AppendHyperLink( ref this FixedBufferWriter w, ReadOnlySpan<char> text, ReadOnlySpan<char> url )
    {
        var saved = w.WrittenLength;
        if( w.Append( HyperLinkPrefix )
            && w.Append( url )
            && w.Append( HyperLinkInfix )
            && w.Append( text )
            && w.Append( HyperLinkSuffix ) )
        {
            return true;
        }
        w.Truncate( saved );
        return false;
    }

    /// <summary>
    /// Moves the cursor to the specified column.
    /// </summary>
    /// <param name="w">This buffer writer.</param>
    /// <param name="columnCount">When 0 or negative, the cursor is moved to the first column (the 1 index).</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool MoveToColumn( ref this FixedBufferWriter w, int columnCount )
    {
        return columnCount <= 1
                ? w.Append( "\u001b[1G" )
                : Append( ref w, columnCount );

        static bool Append( ref FixedBufferWriter w, int columnCount )
        {
            int saved = w.WrittenLength;
            if( w.AppendCSI()
                && w.Append( columnCount )
                && w.Append( 'G' ) )
            {
                return true;
            }
            w.Truncate( saved );
            return false;
        }
    }

    /// <summary>
    /// Moves the cursor up or down from its current position.
    /// </summary>
    /// <param name="w">This buffer writer.</param>
    /// <param name="deltaLine">Positive to move the cursor down, negative to move the cursor up. 0 is a no-op.</param>
    /// <param name="resetColumn">
    /// True to move the cursor at the start of the target line.
    /// By default, the cursor stays on its current column.
    /// </param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool MoveToRelativeLine( ref this FixedBufferWriter w, int deltaLine, bool resetColumn = false )
    {
        return deltaLine == 1
                ? w.Append( resetColumn ? "\u001b[1E" : "\u001b[1B" )
                : deltaLine == -1
                ? w.Append( resetColumn ? "\u001b[1F" : "\u001b[1A" )
                : deltaLine > 0
                ? Append( ref w, deltaLine, resetColumn ? 'E' : 'B' )
                : deltaLine < 0
                ? Append( ref w, -deltaLine, resetColumn ? 'F' : 'A' )
                : true;

        static bool Append( ref FixedBufferWriter w, int deltaLine, char move )
        {
            int saved = w.WrittenLength;
            if( w.AppendCSI()
                && w.Append( deltaLine )
                && w.Append( move ) )
            {
                return true;
            }
            w.Truncate( saved );
            return false;
        }
    }

    /// <summary>
    /// Writes the foreground and background colors and optionally a text effect.
    /// </summary>
    /// <param name="w">This writer.</param>
    /// <param name="c">The color.</param>
    /// <param name="effect">The effect to apply.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool AppendStyle( ref this FixedBufferWriter w, Color c, TextEffect effect = TextEffect.Ignore )
    {
        int saved = w.WrittenLength;
        if( w.AppendCSI()
            // Must come first: setting effect emits a 0 to reset the effect but
            // that also resets the colors.
            && (effect == TextEffect.Ignore
                || (AppendEffectArguments( ref w, effect ) && w.Append( ';' ) ))
            && w.AppendColorArgument( c.ForeColor, false )
            && w.Append( ';' )
            && w.AppendColorArgument( c.BackColor, true )
            && w.Append( 'm' ) )
        {
            return true;
        }
        w.Truncate( saved );
        return false;

        static bool AppendEffectArguments( ref FixedBufferWriter w, TextEffect effect )
        {
            int saved = w.WrittenLength;
            if( w.AppendRegularTextArgument()
                && (effect == TextEffect.Regular
                    || (
                         ((effect & TextEffect.Bold) == 0) || (w.Append( ';' ) && w.AppendBoldTextArgument( true ))
                         && ((effect & TextEffect.Italic) == 0) || (w.Append( ';' ) && w.AppendItalicTextArgument( true ))
                         && ((effect & TextEffect.Underline) == 0) || (w.Append( ';' ) && w.AppendUnderlineTextArgument( true ))
                         && ((effect & TextEffect.Strikethrough) == 0) || (w.Append( ';' ) && w.AppendStrikeThroughTextArgument( true ))
                         && ((effect & TextEffect.Blink) == 0) || (w.Append( ';' ) && w.AppendBlinkTextArgument( true ))
                       )) )
            {
                return true;
            }
            w.Truncate( saved );
            return false;
        }

    }

    /// <summary>
    /// Writes the optimized diff between <paramref name="current"/> and <paramref name="style"/>. 
    /// </summary>
    /// <param name="w">This writer.</param>
    /// <param name="current">The current style to consider.</param>
    /// <param name="style">The new style that must replace the current one.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool AppendTextStyleDiff( ref this FixedBufferWriter w, TextStyle current, TextStyle style )
    {
        int saved = w.WrittenLength;
        if( !w.AppendCSI() ) return false;
        int startContent = saved + 2;
        if( current.Color != style.Color && !style.IgnoreColor )
        {
            if( current.Color.ForeColor != style.Color.ForeColor )
            {
                w.AppendColorArgument( style.Color.ForeColor, false );
            }
            if( current.Color.BackColor != style.Color.BackColor )
            {
                if( w.WrittenLength > startContent ) w.Append( ';' );
                w.AppendColorArgument( style.Color.BackColor, true );
            }
        }
        if( current.Effect != style.Effect && style.Effect != TextEffect.Ignore )
        {
            if( style.Effect == TextEffect.Regular )
            {
                if( w.WrittenLength > startContent ) w.Append( ';' );
                w.AppendRegularTextArgument();
            }
            else
            {
                if( (current.Effect & TextEffect.Bold) != (style.Effect & TextEffect.Bold) )
                {
                    if( w.WrittenLength > startContent ) w.Append( ';' );
                    w.AppendBoldTextArgument( (style.Effect & TextEffect.Bold) != 0 );
                }
                if( (current.Effect & TextEffect.Italic) != (style.Effect & TextEffect.Italic) )
                {
                    if( w.WrittenLength > startContent ) w.Append( ';' );
                    w.AppendItalicTextArgument( (style.Effect & TextEffect.Italic) != 0 );
                }
                if( (current.Effect & TextEffect.Underline) != (style.Effect & TextEffect.Underline) )
                {
                    if( w.WrittenLength > startContent ) w.Append( ';' );
                    w.AppendUnderlineTextArgument( (style.Effect & TextEffect.Underline) != 0 );
                }
                if( (current.Effect & TextEffect.Strikethrough) != (style.Effect & TextEffect.Strikethrough) )
                {
                    if( w.WrittenLength > startContent ) w.Append( ';' );
                    w.AppendStrikeThroughTextArgument( (style.Effect & TextEffect.Strikethrough) != 0 );
                }
                if( (current.Effect & TextEffect.Blink) != (style.Effect & TextEffect.Blink) )
                {
                    if( w.WrittenLength > startContent ) w.Append( ';' );
                    w.AppendBlinkTextArgument( (style.Effect & TextEffect.Blink) != 0 );
                }
            }
        }
        if( w.WrittenLength > startContent )
        {
            if( w.Append( 'm' ) ) return true;
            w.Truncate( saved );
            return false;
        }
        // No diff. This should have been tested before calling this. 
        w.Truncate( saved );
        return true;
    }

    /// <summary>
    /// Erases the current line. The cursor remains where it is.
    /// </summary>
    /// <param name="w">The writer.</param>
    /// <param name="span">Specifies which part of the line must be erased.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool EraseLine( ref this FixedBufferWriter w, CursorRelativeSpan span = CursorRelativeSpan.Both )
    {
        int saved = w.WrittenLength;
        if( !w.AppendCSI() || !w.Append( (char)('0' + span) ) || !w.Append( 'K' ) )
        {
            w.Truncate( saved );
            return false;
        }
        return true;
    }

    static bool AppendColorArgument( ref this FixedBufferWriter w, ConsoleColor c, bool background )
    {
        AnsiColor aC = c.FromConsole();
        var (h, l) = Math.DivRem( (int)aC, 10 );
        if( background )
        {
            if( h == 9 )
            {
                return w.Append( '1', '0', (char)('0' + l) );
            }
            ++h;
        }
        return w.Append( (char)('0' + h), (char)('0' + l) );
    }

    static bool AppendRegularTextArgument( ref this FixedBufferWriter w ) => w.Append( '0' );

    static bool AppendBoldTextArgument( ref this FixedBufferWriter w, bool active ) => active ? w.Append( '1' ) : w.Append( '2', '2' );

    static bool AppendUnderlineTextArgument( ref this FixedBufferWriter w, bool active ) => AppendStdDecoration( ref w, active, '4' );

    static bool AppendStrikeThroughTextArgument( ref this FixedBufferWriter w, bool active ) => AppendStdDecoration( ref w, active, '9' );

    static bool AppendItalicTextArgument( ref this FixedBufferWriter w, bool active ) => AppendStdDecoration( ref w, active, '3' );

    static bool AppendBlinkTextArgument( ref this FixedBufferWriter w, bool active ) => AppendStdDecoration( ref w, active, '5' );

    static bool AppendStdDecoration( ref FixedBufferWriter w, bool active, char d ) => active ? w.Append( d ) : w.Append( '2', d );
}
