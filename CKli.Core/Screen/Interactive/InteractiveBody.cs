using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// An interactive body is a simple list of <see cref="IRenderable"/> with
/// optional <see cref="Header"/> and <see cref="Footer"/>.
/// </summary>
public sealed class InteractiveBody
{
    IRenderable? _header;
    readonly List<(IRenderable Renderable, bool NewLine)> _content;
    IRenderable? _footer;

    internal InteractiveBody()
    {
        _content = new List<(IRenderable Renderable, bool NewLine)>();
    }

    /// <summary>
    /// Gets or sets the optional header.
    /// </summary>
    public IRenderable? Header
    {
        get => _header;
        set => _header = value;
    }

    /// <summary>
    /// Gets the content.
    /// </summary>
    public List<(IRenderable Renderable, bool NewLine)> Content => _content;

    internal IEnumerable<IRenderable> GetRenderableContent( ScreenType screenType )
    {
        HorizontalContent? current = null;
        foreach( var (r, newLine) in _content )
        {
            if( current != null )
            {
                current.AddRight( r );
                if( newLine ) yield return current;
                current = null;
            }
            else
            {
                if( newLine )
                {
                    yield return r;
                }
                else
                {
                    current = new HorizontalContent( screenType, r );
                }
            }
        }
        if(  current != null ) yield return current;
    }

    /// <summary>
    /// Gets or sets the optional footer.
    /// </summary>
    public IRenderable? Footer
    {
        get => _footer;
        set => _footer = value;
    }

    internal void Clear()
    {
        _header = null;
        _content.Clear();
        _footer = null;
    }
}
