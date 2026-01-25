using System;
using System.Diagnostics;
using System.Text;

namespace CKli.Core;

/// <summary>
/// A string renderer that renders text styles diff (colors and effect in [brackets]) and end of line ⮐.
/// <para>
/// It exposes a single <see cref="Render(IRenderable)"/> static method.
/// </para>
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class DebugRenderer : IRenderTarget
{
    TextStyle _current;

    readonly StringBuilder _b;

    DebugRenderer( StringBuilder b )
    {
        _b = b;
        _current = TextStyle.Default;
    }

    /// <summary>
    /// Renders the renderable as a string.
    /// </summary>
    /// <param name="r">The renderable.</param>
    /// <returns>The rendered string.</returns>
    public static string Render( IRenderable r )
    {
        var b = new DebugRenderer( new StringBuilder() );
        r.Render( b );
        return b.ToString();
    }

    ScreenType IRenderTarget.ScreenType => ScreenType.Default;

    void IRenderTarget.BeginUpdate() { }

    void IRenderTarget.EndUpdate() { }

    void IRenderTarget.EndOfLine( bool newLine, bool fill )
    {
        _current = WriteDiffStyle( _b, _current, TextStyle.Default );
        if( newLine ) _b.Append( '⮐' ).AppendLine();
    }

    void IRenderTarget.Write( ReadOnlySpan<char> text, TextStyle style )
    {
        if( _current != style )
        {
            var c = _current.OverrideWith( style );
            if( _current != c )
            {
                _current = WriteDiffStyle( _b, _current, c );
            }
        }
        _b.Append( text );

    }

    static TextStyle WriteDiffStyle( StringBuilder b, TextStyle current, TextStyle style )
    {
        bool hasStarted = false;

        static void Start( StringBuilder b, ref bool hasStarted )
        {
            if( hasStarted ) b.Append( ',' );
            else
            {
                b.Append( '[' );
                hasStarted = true;
            }
        }

        if( current.Color != style.Color && !style.IgnoreColor )
        {
            if( current.Color.ForeColor != style.Color.ForeColor )
            {
                Start( b, ref hasStarted );
                b.Append( style.Color.ForeColor.ToString().ToUpperInvariant() );
            }
            if( current.Color.BackColor != style.Color.BackColor )
            {
                Start( b, ref hasStarted );
                b.Append( style.Color.BackColor.ToString().ToLowerInvariant() );
            }
        }
        if( current.Effect != style.Effect && style.Effect != TextEffect.Ignore )
        {
            if( style.Effect == TextEffect.Regular )
            {
                Start( b, ref hasStarted );
                b.Append( "Regular" );
            }
            else
            {
                if( (current.Effect & TextEffect.Bold) != (style.Effect & TextEffect.Bold) )
                {
                    Start( b, ref hasStarted );
                    b.Append( (style.Effect & TextEffect.Bold) != 0 ? "Bold" : "no-Bold" );
                }
                if( (current.Effect & TextEffect.Italic) != (style.Effect & TextEffect.Italic) )
                {
                    Start( b, ref hasStarted );
                    b.Append( (style.Effect & TextEffect.Italic) != 0 ? "Italic" : "no-Italic" );
                }
                if( (current.Effect & TextEffect.Underline) != (style.Effect & TextEffect.Underline) )
                {
                    Start( b, ref hasStarted );
                    b.Append( (style.Effect & TextEffect.Underline) != 0 ? "Underline" : "no-Underline" );
                }
                if( (current.Effect & TextEffect.Strikethrough) != (style.Effect & TextEffect.Strikethrough) )
                {
                    Start( b, ref hasStarted );
                    b.Append( (style.Effect & TextEffect.Strikethrough) != 0 ? "Strikethrough" : "no-Strikethrough" );
                }
                if( (current.Effect & TextEffect.Blink) != (style.Effect & TextEffect.Blink) )
                {
                    Start( b, ref hasStarted );
                    b.Append( (style.Effect & TextEffect.Blink) != 0 ? "Blink" : "no-Blink" );
                }
            }
        }
        if( hasStarted ) b.Append( ']' );
        return style;
    }

    public override string ToString() => _b.ToString();
}
