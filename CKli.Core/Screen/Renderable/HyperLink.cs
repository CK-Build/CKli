using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// An HyperLink with a <see cref="Content"/> (can be any renderable) and a <see cref="Target"/> url.
/// </summary>
public sealed class HyperLink : IRenderable
{
    readonly IRenderable _content;
    readonly Uri _target;

    /// <summary>
    /// Initializes a new <see cref="HyperLink"/>.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="target">The target url.</param>
    public HyperLink( IRenderable content, Uri target )
    {
        _content = content;
        _target = target;
    }

    /// <inheritdoc />
    public ScreenType ScreenType => _content.ScreenType;

    /// <inheritdoc />
    public int Height => _content.Height;

    /// <inheritdoc />
    public int Width => _content.Width;

    /// <inheritdoc />
    public int MinWidth => _content.MinWidth;

    /// <inheritdoc />
    public int NominalWidth => _content.NominalWidth;

    /// <summary>
    /// Gets this hyper link content.
    /// </summary>
    public IRenderable Content => _content;

    /// <inheritdoc />
    public IRenderable SetWidth( int width, bool allowWider )
    {
        var newContent = _content.SetWidth( width, allowWider );
        return newContent == _content ? this : new HyperLink( newContent, _target );
    }

    /// <summary>
    /// Gets the target url.
    /// </summary>
    public Uri Target => _target;

    /// <summary>
    /// Returns a HyperLink with an updated url target.
    /// </summary>
    /// <param name="target">The new target.</param>
    /// <returns>This hyperlink or a new one.</returns>
    public HyperLink WithTarget( Uri target ) => _target == target ? this : new HyperLink( _content, target );

    /// <summary>
    /// Returns a HyperLink with an updated content.
    /// </summary>
    /// <param name="content">The new content.</param>
    /// <returns>This hyperlink or a new one.</returns>
    public HyperLink WithContent( IRenderable content ) => _content == content ? this : new HyperLink( content, _target );

    /// <inheritdoc />
    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    /// <inheritdoc />
    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        Throw.CheckArgument( line >= 0 && line < actualHeight && actualHeight >= Height );
        if( line < actualHeight )
        {
            if( ScreenType.HasAnsiLink )
            {
                _ = new HyperLinkRenderer( parent, _content, line, actualHeight, _target );
            }
            else
            {
                _ = new SegmentRenderer( parent, _content, line, actualHeight );
            }
        }
    }

    sealed class HyperLinkRenderer( SegmentRenderer parent, IRenderable content, int line, int actualHeight, Uri target )
        : SegmentRenderer( parent, content, line, actualHeight )
    {
        protected override void Render()
        {
            Target.Write( AnsiCodes.HyperLinkPrefix, default );
            Target.Write( target.ToString(), default );
            Target.Write( AnsiCodes.HyperLinkInfix, default );
            RenderContent();
            Target.Write( AnsiCodes.HyperLinkSuffix, default );
        }
    }
}
