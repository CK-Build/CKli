using CK.Core;
using System;

namespace CKli.Core;

public sealed partial class ContentBox
{
    public SegmentRenderer CollectRenderer( int line, SegmentRenderer previous )
    {
        Throw.DebugAssert( line >= 0 );
        line -= _padding.Top;
        SegmentRenderer renderer = line < _content.Height
                                        ? new LeftAlignRenderer( previous, Width, _style, _padding )
                                        : new PaddingRenderer( previous, Width, _style );
        return _content.CollectRenderer( line, renderer );
    }

    sealed class PaddingRenderer( SegmentRenderer previous, int length, TextStyle style ) : SegmentRenderer( previous, length, style )
    {
        protected override void Render( IRenderTarget target ) => RenderPadding( Length, target, FinalStyle );
    }

    sealed class LeftAlignRenderer( SegmentRenderer previous, int length, TextStyle style, Padding padding ) : SegmentRenderer( previous, length, style )
    {
        protected override void Render( IRenderTarget target )
        {
            if( padding.Left > 0 ) RenderPadding( padding.Left, target, FinalStyle );
            var content = Next;
            Throw.DebugAssert( content != null );
            content.Render();
            int rightPad = Length - content.Length - padding.Left;
            if( rightPad > 0 ) RenderPadding( rightPad, target, FinalStyle );
        }
    }

    sealed class RightAlignRenderer( SegmentRenderer previous, int length, Padding padding, TextStyle style ) : SegmentRenderer( previous, length, style )
    {
        protected override void Render( IRenderTarget target )
        {
            var content = Next;
            Throw.DebugAssert( content != null );
            int leftPad = padding.Left + Length - content.Length;
            if( leftPad > 0 ) RenderPadding( leftPad, target, FinalStyle );
            content.Render();
            if( padding.Right > 0 ) RenderPadding( padding.Right, target, FinalStyle );
        }
    }

    sealed class CenterAlignRenderer( SegmentRenderer previous, int length, Padding padding, TextStyle style ) : SegmentRenderer( previous, length, style )
    {
        protected override void Render( IRenderTarget target )
        {
            var content = Next;
            Throw.DebugAssert( content != null );
            int pad = Length - content.Length;
            int padLeft = padding.Left + pad >> 1;
            if( padLeft > 0 ) RenderPadding( padLeft, target, FinalStyle );
            content.Render();
            int padRight = pad >> 1 + (pad & 1) + padding.Right;
            if( padRight > 0 ) RenderPadding( padRight, target, FinalStyle );
        }
    }

    static ReadOnlySpan<char> _whites => [' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' '];

    static void RenderPadding( int pad, IRenderTarget target, TextStyle style )
    {
        while( pad > _whites.Length )
        {
            target.Append( _whites, style );
            pad -= _whites.Length;
        }
        if( pad > 0 ) target.Append( _whites.Slice( 0, pad ), style );
    }

}


