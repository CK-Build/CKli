using System;
using System.Text;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    delegate void CoreTextWriter( ReadOnlySpan<char> text );

    sealed class RenderTarget : IRenderTarget
    {
        readonly StringBuilder _buffer;
        readonly CoreTextWriter _buffered;
        readonly CoreTextWriter _unbuffered;
        int _updateCount;
        TextStyle _current;

        public RenderTarget( CoreTextWriter writer )
        {
            _buffer = new StringBuilder();
            _buffered = text => _buffer.Append( text );
            _unbuffered = writer;
            _current = TextStyle.Default;
            // Initializes our default colors.
            Span<char> b = stackalloc char[16];
            var w = new FixedBufferWriter( b );
            w.AppendStyle( _current.Color, _current.Effect );
            writer( w.Text );
        }

        public void BeginUpdate() => _updateCount++;

        public void EndUpdate()
        {
            if( --_updateCount == 0 )
            {
                _unbuffered( _buffer.ToString() );
                _buffer.Clear();
            }
        }

        CoreTextWriter Writer => _updateCount > 0 ? _buffered : _unbuffered;

        public ScreenType ScreenType => _screenType;

        public void RawWrite( ReadOnlySpan<char> text ) => Writer( text );

        public void Write( ReadOnlySpan<char> text, TextStyle style )
        {
            var w = Writer;
            if( _current != style )
            {
                var c = _current.OverrideWith( style );
                if( _current != c )
                {
                    _current = WriteDiff( w, _current, c );
                }
            }
            w( text );
        }

        static TextStyle WriteDiff( CoreTextWriter writer, TextStyle current, TextStyle style )
        {
            Span<char> styleBuffer = stackalloc char[64];
            var w = new FixedBufferWriter( styleBuffer );
            w.AppendTextStyleDiff( current, style );
            writer( w.Text );
            return style;
        }

        public void EndOfLine( bool newLine )
        {
            _current = TextStyle.Default;
            Span<char> b = stackalloc char[16];
            var w = new FixedBufferWriter( b );
            w.AppendStyle( _current.Color, _current.Effect );
            if( newLine ) w.Append( '\n' );
            RawWrite( w.Text );
        }
    }

}
