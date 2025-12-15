using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// An interactive body is the <see cref="Logs"/> collected by the animations with
/// optional <see cref="Header"/> and <see cref="Footer"/>.
/// </summary>
public sealed class InteractiveHeader
{
    IRenderable? _header;
    VerticalContent? _logs;
    IRenderable? _footer;

    internal InteractiveHeader()
    {
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
    /// Gets or sets the logs entries.
    /// </summary>
    public VerticalContent? Logs
    {
        get => _logs;
        set => _logs = value;
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
        _logs = null;
        _footer = null;
    }
}
