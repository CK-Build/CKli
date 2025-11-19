using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// An interactive body is a simple list of <see cref="IRenderable"/> with
/// optional <see cref="Header"/> and <see cref="Footer"/>.
/// </summary>
public sealed class InteractiveBody
{
    IRenderable? _header;
    readonly List<IRenderable> _content;
    IRenderable? _footer;

    internal InteractiveBody()
    {
        _content = new List<IRenderable>();
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
    public List<IRenderable> Content => _content;

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
