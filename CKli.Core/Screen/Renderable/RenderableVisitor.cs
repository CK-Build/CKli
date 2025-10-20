using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// Transformer visitor pattern.
/// </summary>
public abstract class RenderableVisitor
{
    public virtual IRenderable Visit( ContentBox b )
    {
        var newContent = b.Content.Accept( this );
        return newContent == b.Content ? b : b.WithContent( newContent );
    }

    public virtual IRenderable Visit( HorizontalContent h )
    {
        ImmutableArray<IRenderable>.Builder? b = null;
        for( int i = 0; i < h.Cells.Length; i++ )
        {
            IRenderable? c = h.Cells[i];
            var newC = c.Accept( this );
            if( newC != c )
            {
                if( b == null )
                {
                    b = ImmutableArray.CreateBuilder<IRenderable>( h.Cells.Length );
                    b.AddRange( h.Cells, i );
                }
                if( newC.Width > 0 ) b.Add( newC );
            }
            else
            {
                b?.Add( c );
            }
        }
        return b == null
                ? h
                : b.Count == 0
                    ? h.ScreenType.Unit
                    : new HorizontalContent( h.ScreenType, b.DrainToImmutable() );
    }

    public virtual IRenderable Visit( VerticalContent v )
    {
        ImmutableArray<IRenderable>.Builder? b = null;
        for( int i = 0; i < v.Cells.Length; i++ )
        {
            IRenderable? c = v.Cells[i];
            var newC = c.Accept( this );
            if( newC != c )
            {
                if( b == null )
                {
                    b = ImmutableArray.CreateBuilder<IRenderable>( v.Cells.Length );
                    b.AddRange( v.Cells, i );
                }
                if( newC.Height > 0 ) b.Add( newC );
            }
            else
            {
                b?.Add( c );
            }
        }
        return b == null
                ? v
                : b.Count == 0
                    ? v.ScreenType.Unit
                    : new VerticalContent( v.ScreenType, b.DrainToImmutable() );
    }

    public virtual IRenderable Visit( TextBlock t ) => t;

    public virtual IRenderable Visit( Collapsable c )
    {
        var newContent = c.Content.Accept( this );
        return newContent == c.Content ? c : c.WithContent( newContent );
    }

    public virtual IRenderable Visit( HyperLink c )
    {
        var newContent = c.Content.Accept( this );
        return newContent == c.Content ? c : c.WithContent( newContent );
    }
}
