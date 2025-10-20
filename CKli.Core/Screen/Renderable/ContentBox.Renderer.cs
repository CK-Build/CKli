using CK.Core;
using System;

namespace CKli.Core;

public sealed partial class ContentBox
{
    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        Throw.CheckArgument( line >= 0 && line < actualHeight && actualHeight >= Height );
        Throw.DebugAssert( line >= 0 );
        line -= _margin.Top;
        if( line < 0 )
        {
            _ = new PaddingRenderer( parent, Width, parent.FinalStyle );
        }
        else
        {
            line -= _padding.Top;
            if( line < 0 )
            {
                _ = new PaddingRenderer( parent, Width, _style );
            }
            else
            {
                var lineBelow = line - _content.Height;
                if( lineBelow < 0 )
                {
                    if( _align.IsRight() )
                    {
                        _ = new RightAlignRenderer( parent, Width, _content, line, actualHeight, _style, _padding, _margin );
                    }
                    else if( _align.IsCenter() )
                    {
                        _ = new CenterAlignRenderer( parent, Width, _content, line, actualHeight, _style, _padding, _margin );
                    }
                    else
                    {
                        _ = new LeftAlignRenderer( parent, Width, _content, line, actualHeight, _style, _padding, _margin );
                    }
                }
                else
                {
                    lineBelow -= _padding.Bottom;
                    if( lineBelow < 0 )
                    {
                        _ = new PaddingRenderer( parent, Width, _style );
                    }
                    else
                    {
                        _ = new PaddingRenderer( parent, Width, parent.FinalStyle );
                    }
                }
            }
        }
    }

    sealed class PaddingRenderer( SegmentRenderer previous, int length, TextStyle style ) : SegmentRenderer( previous, length, style )
    {
        protected override void Render() => RenderPadding( Length, Target, FinalStyle );
    }

    sealed class LeftAlignRenderer( SegmentRenderer parent, int length, IRenderable content, int line, int actualHeight, TextStyle style, Filler padding, Filler margin )
        : SegmentRenderer( parent, length, content, line, actualHeight, style )
    {
        protected override void Render()
        {
            if( margin.Left > 0 ) RenderPadding( margin.Left, Target, ParentFinalStyle );
            if( padding.Left > 0 ) RenderPadding( padding.Left, Target, FinalStyle );
            RenderContent();
            int rightPad = Length - ContentLength - padding.Left - margin.Left;
            if( rightPad > 0 ) RenderPadding( rightPad, Target, FinalStyle );
            if( margin.Right > 0 ) RenderPadding( margin.Right, Target, ParentFinalStyle );
        }
    }

    sealed class RightAlignRenderer( SegmentRenderer parent, int length, IRenderable content, int line, int actualHeight, TextStyle style, Filler padding, Filler margin )
        : SegmentRenderer( parent, length, content, line, actualHeight, style )
    {
        protected override void Render()
        {
            if( margin.Left > 0 ) RenderPadding( margin.Left, Target, ParentFinalStyle );
            int leftPad = padding.Left + Length - ContentLength - margin.Left;
            if( leftPad > 0 ) RenderPadding( leftPad, Target, FinalStyle );
            RenderContent();
            if( padding.Right > 0 ) RenderPadding( padding.Right, Target, FinalStyle );
            if( margin.Right > 0 ) RenderPadding( margin.Right, Target, ParentFinalStyle );
        }
    }

    sealed class CenterAlignRenderer( SegmentRenderer parent, int length, IRenderable content, int line, int actualHeight, TextStyle style, Filler padding, Filler margin )
        : SegmentRenderer( parent, length, content, line, actualHeight, style )
    {
        protected override void Render()
        {
            if( margin.Left > 0 ) RenderPadding( margin.Left, Target, ParentFinalStyle );
            int pad = Length - ContentLength - margin.Left - margin.Right;
            int padLeft = padding.Left + (pad >> 1);
            if( padLeft > 0 ) RenderPadding( padLeft, Target, FinalStyle );
            RenderContent();
            int padRight = (pad >> 1) + (pad & 1) + padding.Right;
            if( padRight > 0 ) RenderPadding( padRight, Target, FinalStyle );
            if( margin.Right > 0 ) RenderPadding( margin.Right, Target, ParentFinalStyle );
        }
    }

    static ReadOnlySpan<char> _whites => [' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' ', ' '];

    static void RenderPadding( int pad, IRenderTarget target, TextStyle style )
    {
        while( pad > _whites.Length )
        {
            target.Write( _whites, style );
            pad -= _whites.Length;
        }
        if( pad > 0 ) target.Write( _whites.Slice( 0, pad ), style );
    }

}


