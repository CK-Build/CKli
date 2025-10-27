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
        return h.ApplyTransform( r => r.Accept( this ) );
    }

    public virtual IRenderable Visit( VerticalContent v ) 
    {
        return v.ApplyTransform( r => r.Accept( this ) );
    }

    public virtual IRenderable Visit( TableLayout t ) 
    {
        var rows = t.Rows.Accept( this );
        return rows != t.Rows ? TableLayout.Create( rows, t.Columns ) : t;
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
