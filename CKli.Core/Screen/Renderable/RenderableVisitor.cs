namespace CKli.Core;

/// <summary>
/// Transformer visitor pattern.
/// </summary>
public abstract class RenderableVisitor
{
    /// <summary>
    /// Visits the <see cref="ContentBox.Content"/>.
    /// </summary>
    /// <param name="b">The box to visit.</param>
    /// <returns>The visit result.</returns>
    public virtual IRenderable Visit( ContentBox b )
    {
        var newContent = b.Content.Accept( this );
        return newContent == b.Content ? b : b.WithContent( newContent );
    }

    /// <summary>
    /// Visits a <see cref="HorizontalContent"/> by calling <see cref="HorizontalContent.ApplyTransform(System.Func{IRenderable, IRenderable?})"/>.
    /// </summary>
    /// <param name="h">The horizontal content to visit.</param>
    /// <returns>The visit result.</returns>
    public virtual IRenderable Visit( HorizontalContent h )
    {
        return h.ApplyTransform( r => r.Accept( this ) );
    }

    /// <summary>
    /// Visits a <see cref="VerticalContent"/> by calling <see cref="VerticalContent.ApplyTransform(System.Func{IRenderable, IRenderable?})"/>.
    /// </summary>
    /// <param name="v">The vertical content to visit.</param>
    /// <returns>The visit result.</returns>
    public virtual IRenderable Visit( VerticalContent v ) 
    {
        return v.ApplyTransform( r => r.Accept( this ) );
    }

    /// <summary>
    /// Visits a <see cref="TableLayout"/> by visiting its <see cref="TableLayout.Rows"/>.
    /// </summary>
    /// <param name="t">The table layout to visit.</param>
    /// <returns>The visit result.</returns>
    public virtual IRenderable Visit( TableLayout t ) 
    {
        var rows = t.Rows.Accept( this );
        return rows != t.Rows ? TableLayout.Create( rows, t.Columns ) : t;
    }

    /// <summary>
    /// Visits a <see cref="TextBlock"/>.
    /// </summary>
    /// <param name="t">The TextBlock to visit.</param>
    /// <returns>The visit result.</returns>
    public virtual IRenderable Visit( TextBlock t ) => t;

    /// <summary>
    /// Visits the <see cref="Collapsable.Content"/>.
    /// </summary>
    /// <param name="c">The collapsable to visit.</param>
    /// <returns>The visit result.</returns>
    public virtual IRenderable Visit( Collapsable c )
    {
        var newContent = c.Content.Accept( this );
        return newContent == c.Content ? c : c.WithContent( newContent );
    }

    /// <summary>
    /// Visits the <see cref="HyperLink.Content"/>.
    /// </summary>
    /// <param name="c">The hyperlink to visit.</param>
    /// <returns>The visit result.</returns>
    public virtual IRenderable Visit( HyperLink c )
    {
        var newContent = c.Content.Accept( this );
        return newContent == c.Content ? c : c.WithContent( newContent );
    }
}
