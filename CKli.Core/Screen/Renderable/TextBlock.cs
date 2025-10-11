using CK.Core;
using LibGit2Sharp;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.Marshalling;

namespace CKli.Core;

public abstract class TextBlock : IRenderable
{
    public const int MinWidth = 15;

    readonly internal string _text;
    readonly internal int _width;
    readonly TextEffect _effect;

    public static readonly TextBlock EmptyString = new Empty();

    private protected TextBlock( string text, int width, TextEffect effect )
    {
        _text = text;
        _width = width;
        _effect = effect;
    }

    public abstract int Height { get; }

    public int Width => _width;

    public abstract int RenderLine( int line, IRenderTarget target, RenderContext context );

    public abstract TextBlock SetWidth( int width );

    /// <summary>
    /// Creates a mono or multi line block of text.
    /// </summary>
    /// <param name="text">The raw text.</param>
    /// <returns>The renderable.</returns>
    [return: NotNullIfNotNull( nameof( text ) )]
    public static TextBlock? FromText( string? text )
    {
        if( text == null ) return null;
        var sText = text.AsSpan();
        sText = sText.Trim();
        if( sText.Length == 0 ) return EmptyString;
        var lineCount = sText.Count( '\n' ) + 1;
        if( lineCount == 1 )
        {
            return new MonoLine( text, sText.Length );
        }
        int witdh = 0;
        var b = ImmutableArray.CreateBuilder<(int, int)>( lineCount );
        int start = 0;
        int idx = sText.IndexOf( "\n" );
        do
        {
            int trimS = 0;
            int trimE = 0;
            while( trimS < idx && char.IsWhiteSpace( sText[trimS] ) ) trimS++;
            int len;
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
        b.Add( (start, sText.Length) );
        return new MultiLine( text, b.MoveToImmutable(), witdh );

    }

    sealed class MonoLine : TextBlock
    {
        static readonly SearchValues<char> _cutChars = SearchValues.Create( " ,;!-?." );

        public MonoLine( string text, int width, TextEffect effect = TextEffect.Regular )
            : base( text, width, effect )
        {
        }

        public override int Height => 1;

        public override int RenderLine( int line, IRenderTarget target, RenderContext context )
        {
            Throw.DebugAssert( line >= 0 );
            if( line == 0 )
            {
                target.Append( _text.AsSpan( 0, _width ), context.GetTextStyle( _effect ) );
                return _width;
            }
            return 0;
        }

        public override TextBlock SetWidth( int width )
        {
            if( width < MinWidth || width >= _width ) return this;
            var rangeCollector = ImmutableArray.CreateBuilder<(int Start, int Length)>( width / 8 );
            int w = SetWidthLine( width, rangeCollector, 0, _text.AsSpan( 0, _width ) );
            return new LinesAdjustedWidth( this, rangeCollector.DrainToImmutable(), w, _effect );
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

        public override string ToString() => _text;
    }

    static int RenderMultiLine( int line, string text, IRenderTarget target, ImmutableArray<(int Start, int Length)> lines, TextStyle style )
    {
        Throw.DebugAssert( line >= 0 );
        if( line < lines.Length )
        {
            var (start, len) = lines[line];
            target.Append( text.AsSpan( start, len ), style );
            return len;
        }
        return 0;
    }

    sealed class MultiLine : TextBlock
    {
        readonly ImmutableArray<(int Start, int Length)> _lines;

        public MultiLine( string text, ImmutableArray<(int, int)> lines, int width, TextEffect effect = TextEffect.Regular )
            : base( text, width, effect )
        {
            _lines = lines;
        }

        public override int Height => _lines.Length;

        public override int RenderLine( int line, IRenderTarget target, RenderContext context ) => RenderMultiLine( line, _text, target, _lines, context.GetTextStyle( _effect ) );

        public override TextBlock SetWidth( int width )
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
            return new LinesAdjustedWidth( this, rangeCollector.DrainToImmutable(), finalWidth, _effect );
        }

        public override string ToString() => _text;
    }

    sealed class LinesAdjustedWidth : TextBlock
    {
        readonly TextBlock _origin;
        readonly ImmutableArray<(int Start, int Length)> _lines;

        public LinesAdjustedWidth( TextBlock origin, ImmutableArray<(int Start, int Length)> lines, int width, TextEffect effect )
            : base( origin._text, width, effect )
        {
            _origin = origin;
            _lines = lines;
        }

        public override int Height => _lines.Length;

        public override int RenderLine( int line, IRenderTarget target, RenderContext context ) => RenderMultiLine( line, _text, target, _lines, context.GetTextStyle( _effect ) );

        public override TextBlock SetWidth( int width ) => _origin.SetWidth( width );
    }

    sealed class Empty : TextBlock
    {
        public Empty()
            : base( string.Empty, 0, TextEffect.Ignore )
        {
        }

        public override int Height => 1;

        public override int RenderLine( int line, IRenderTarget target, RenderContext context ) => 0;

        public override TextBlock SetWidth( int width ) => this;
    }
}

