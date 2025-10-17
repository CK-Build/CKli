using CK.Core;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

public abstract class TextBlock : IRenderable
{
    public const int MinWidth = 15;

    readonly internal string _text;
    readonly internal int _width;
    readonly TextStyle _style;

    /// <summary>
    /// An empty singleton empty block: typically used as a new line.
    /// </summary>
    public static readonly TextBlock EmptyString = new Empty();

    private protected TextBlock( string text, int width, TextStyle style )
    {
        _text = text;
        _width = width;
        _style = style;
    }

    public abstract int Height { get; }

    public int Width => _width;

    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        // Texts have no notion of context. Lines are drawn where they are told to draw,
        // padding and margins are not their concern (it's the Box responsibility): we
        // ignore the actualHeight.
        BuildSegmentTree( line, parent );
    }

    protected abstract void BuildSegmentTree( int line, SegmentRenderer parent );

    public abstract TextBlock SetTextWidth( int width );

    /// <summary>
    /// Creates a mono or multi line block of text.
    /// </summary>
    /// <param name="text">The raw text.</param>
    /// <param name="style">Optional text style.</param>
    /// <returns>The renderable.</returns>
    [return: NotNullIfNotNull( nameof( text ) )]
    public static TextBlock? FromText( string? text, TextStyle style = default )
    {
        if( text == null ) return null;
        var sText = text.AsSpan();
        sText = sText.TrimStart();
        int start = text.Length - sText.Length;
        sText = sText.TrimEnd();
        if( sText.Length == 0 ) return EmptyString;
        var lineCount = sText.Count( '\n' ) + 1;
        if( lineCount == 1 )
        {
            return new MonoLine( text, start, sText.Length, style );
        }
        int witdh = 0;
        var b = ImmutableArray.CreateBuilder<(int, int)>( lineCount );
        int idx = sText.IndexOf( "\n" );
        int trimS;
        int len;
        do
        {
            trimS = 0;
            while( trimS < idx && char.IsWhiteSpace( sText[trimS] ) ) trimS++;
            int trimE = 0;
            if( trimS == idx )
            {
                len = 0;
            }
            else
            {
                trimE = idx - 1;
                while( trimE > 0 && char.IsWhiteSpace( sText[trimE] ) ) trimE--;
                trimE = idx - 1 - trimE;
            }
            len = idx - trimS - trimE;
            if( len > witdh ) witdh = len;
            b.Add( (start + trimS, len) );
            start += ++idx;
            sText = sText.Slice( idx );
            idx = sText.IndexOf( "\n" );
        }
        while( idx >= 0 );
        trimS = 0;
        idx = sText.Length;
        while( trimS < idx && char.IsWhiteSpace( sText[trimS] ) ) trimS++;
        len = idx - trimS;
        if( len > witdh ) witdh = len;
        b.Add( (start + trimS, len) );
        return new MultiLine( text, b.MoveToImmutable(), witdh, style );
    }

    sealed class MonoLine : TextBlock
    {
        static readonly SearchValues<char> _cutChars = SearchValues.Create( " ,;!-?." );

        readonly int _start;

        public MonoLine( string text, int start, int length, TextStyle style )
            : base( text, length, style )
        {
            _start = start;
        }

        public override int Height => 1;

        public override TextBlock SetTextWidth( int width )
        {
            if( width < MinWidth || width >= _width ) return this;
            var rangeCollector = ImmutableArray.CreateBuilder<(int Start, int Length)>( width / 8 );
            int w = SetWidthLine( width, rangeCollector, _start, _text.AsSpan( _start, _width ) );
            return new LinesAdjustedWidth( this, rangeCollector.DrainToImmutable(), w, _style );
        }

        internal static int SetWidthLine( int width, ImmutableArray<(int Start, int Length)>.Builder rangeCollector, int start, ReadOnlySpan<char> line )
        {
            Throw.DebugAssert( line.Trim().Length == line.Length );
            if( line.Length <= width )
            {
                rangeCollector.Add( (start, line.Length) );
                return line.Length;
            }
            int w = 0;
            do
            {
                int newLen;
                if( line[width] == ' ' )
                {
                    newLen = width;
                }
                else
                {
                    newLen = line.Slice( 0, width ).LastIndexOfAny( _cutChars );
                    if( newLen == -1 ) newLen = width;
                }
                rangeCollector.Add( (start, newLen) );
                if( w < newLen ) w = newLen;
                start += newLen;
                line = line.Slice( newLen );
                int trim = 0;
                while( trim < line.Length && char.IsWhiteSpace( line[trim] ) ) trim++;
                start += trim;
                line = line.Slice( trim );
            }
            while( line.Length > width );
            if( line.Length > 0 )
            {
                rangeCollector.Add( (start, line.Length) );
            }
            return w;
        }

        protected override void BuildSegmentTree( int line, SegmentRenderer parent )
        {
            Throw.DebugAssert( line >= 0 );
            if( line == 0 )
            {
                _ = new Renderer( parent, _width, _style, _text );
            }
        }

        sealed class Renderer( SegmentRenderer previous, int length, TextStyle style, string text ) : SegmentRenderer( previous, length, style )
        {
            protected override void Render() => Target.Write( text.AsSpan( 0, Length ), FinalStyle );
        }

        public override string ToString() => _text;
    }


    sealed class MultiLineRenderer( SegmentRenderer previous, TextStyle style, string text, (int Start, int Length) range )
        : SegmentRenderer( previous, range.Length, style )
    {
        protected override void Render() => Target.Write( text.AsSpan( range.Start, range.Length ), FinalStyle );

        public static void Create( SegmentRenderer parent,
                                   int line,
                                   string text,
                                   ImmutableArray<(int Start, int Length)> lines,
                                   TextStyle style )
        {
            Throw.DebugAssert( line >= 0 );
            if( line < lines.Length )
            {
                _ = new MultiLineRenderer( parent, style, text, lines[line] );
            }
        }
    }

    sealed class MultiLine( string text, ImmutableArray<(int, int)> lines, int width, TextStyle style ) : TextBlock( text, width, style )
    {
        readonly ImmutableArray<(int Start, int Length)> _lines = lines;

        public override int Height => _lines.Length;

        protected override void BuildSegmentTree( int line, SegmentRenderer parent ) => MultiLineRenderer.Create( parent, line, _text, _lines, _style );

        public override TextBlock SetTextWidth( int width )
        {
            if( width < MinWidth || width >= _width ) return this;
            var rangeCollector = ImmutableArray.CreateBuilder<(int Start, int Length)>( _lines.Length * 2 );
            int finalWidth = 0;
            for( int i = 0; i < _lines.Length; i++ )
            {
                var (start, len) = _lines[i];
                if( len > width )
                {
                    var w = MonoLine.SetWidthLine( width, rangeCollector, start, _text.AsSpan( start, len ) );
                    if( finalWidth < w ) finalWidth = w;
                }
                else
                {
                    if( finalWidth < len ) finalWidth = len;
                }
            }
            return new LinesAdjustedWidth( this, rangeCollector.DrainToImmutable(), finalWidth, _style );
        }

        public override string ToString() => _text;
    }

    sealed class LinesAdjustedWidth : TextBlock
    {
        readonly TextBlock _origin;
        readonly ImmutableArray<(int Start, int Length)> _lines;

        public LinesAdjustedWidth( TextBlock origin, ImmutableArray<(int Start, int Length)> lines, int width, TextStyle style )
            : base( origin._text, width, style )
        {
            _origin = origin;
            _lines = lines;
        }

        public override int Height => _lines.Length;

        protected override void BuildSegmentTree( int line, SegmentRenderer previous ) => MultiLineRenderer.Create( previous, line, _text, _lines, _style );

        public override TextBlock SetTextWidth( int width ) => _origin.SetTextWidth( width );
    }

    sealed class Empty : TextBlock
    {
        public Empty()
            : base( string.Empty, 0, TextStyle.None )
        {
        }

        public override int Height => 1;

        protected override void BuildSegmentTree( int line, SegmentRenderer parent ) { }

        public override TextBlock SetTextWidth( int width ) => this;
    }
}

