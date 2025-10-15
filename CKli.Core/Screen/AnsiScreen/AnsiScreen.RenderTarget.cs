using System;
using System.IO;
using System.Text;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed class RenderTarget : IRenderTarget
    {
        delegate void WriteText( ReadOnlySpan<char> text );

        readonly StringBuilder _buffer;
        readonly TextWriter _out;
        readonly WriteText _buffered;
        readonly WriteText _unbuffered;
        int _updateCount;
        TextStyle _current;

        public RenderTarget( TextWriter outW )
        {
            _buffer = new StringBuilder();
            _out = outW;
            _buffered = text => _buffer.Append( text );
            _unbuffered = text => _out.Write( text );
        }

        public void BeginUpdate() => _updateCount++;

        public void EndUpdate()
        {
            if( --_updateCount == 0 )
            {
                _out.Write( _buffer.ToString() );
                _buffer.Clear();
            }
        }

        WriteText Writer => _updateCount > 0 ? _buffered : _unbuffered;

        public void Append( ReadOnlySpan<char> text, TextStyle style )
        {
            var w = Writer;
            if( _current != style )
            {
                var c = _current.OverrideWith( style );
                WriteDiff( w, _current, c );
                _current = c;
            }
            w( text );
        }

        void WriteDiff( WriteText w, TextStyle current, TextStyle style )
        {
            Span<char> chars = stackalloc char[64];
            var head = AnsiCodes.WriteCSI( chars );
            if( current.Color != style.Color && !style.IgnoreColor )
            {
                if( current.Color.ForeColor != style.Color.ForeColor )
                {
                    head = AnsiCodes.WriteColor( head, style.Color.ForeColor, false );
                }
                if( current.Color.BackColor != style.Color.BackColor )
                {
                    if( head.Length > 2 ) head = AnsiCodes.WriteSemiColon( head );
                    head = AnsiCodes.WriteColor( head, style.Color.BackColor, true );
                }
            }
            if( current.Effect != style.Effect && style.Effect != TextEffect.Ignore )
            {
                if( style.Effect == TextEffect.Regular )
                {
                    if( head.Length > 2 ) head = AnsiCodes.WriteSemiColon( head );
                    head = AnsiCodes.WriteRegular( head );
                }
                else
                {
                    if( (current.Effect & TextEffect.Bold) != (style.Effect & TextEffect.Bold) )
                    {
                        if( head.Length > 2 ) head = AnsiCodes.WriteSemiColon( head );
                        head = AnsiCodes.WriteBold( head, (style.Effect & TextEffect.Bold) != 0 );
                    }
                    if( (current.Effect & TextEffect.Italic) != (style.Effect & TextEffect.Italic) )
                    {
                        if( head.Length > 2 ) head = AnsiCodes.WriteSemiColon( head );
                        head = AnsiCodes.WriteItalic( head, (style.Effect & TextEffect.Italic) != 0 );
                    }
                    if( (current.Effect & TextEffect.Underline) != (style.Effect & TextEffect.Underline) )
                    {
                        if( head.Length > 2 ) head = AnsiCodes.WriteSemiColon( head );
                        head = AnsiCodes.WriteUnderline( head, (style.Effect & TextEffect.Underline) != 0 );
                    }
                    if( (current.Effect & TextEffect.Strikethrough) != (style.Effect & TextEffect.Strikethrough) )
                    {
                        if( head.Length > 2 ) head = AnsiCodes.WriteSemiColon( head );
                        head = AnsiCodes.WriteStrikeThrough( head, (style.Effect & TextEffect.Strikethrough) != 0 );
                    }
                    if( (current.Effect & TextEffect.Blink) != (style.Effect & TextEffect.Blink) )
                    {
                        if( head.Length > 2 ) head = AnsiCodes.WriteSemiColon( head );
                        head = AnsiCodes.WriteBlink( head, (style.Effect & TextEffect.Blink) != 0 );
                    }
                }
            }
            if( head.Length > 2 ) head = AnsiCodes.WriteCommand( head, 'm' );

            var writeLen = chars.Length - head.Length;
            if( writeLen > 0 ) w( chars.Slice( 0, writeLen ) );
        }

        public void EndOfLine() => Append( Environment.NewLine, TextStyle.Default );
    }

}
