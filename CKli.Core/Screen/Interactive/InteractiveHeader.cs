using System.Collections.Generic;

namespace CKli.Core;

public sealed class InteractiveHeader
{
    IRenderable? _header;
    readonly List<IRenderable> _logs;
    IRenderable? _footer;

    internal InteractiveHeader()
    {
        _logs = new List<IRenderable>();
    }

    public IRenderable? Header
    {
        get => _header;
        set => _header = value;
    }

    public List<IRenderable> Logs => _logs;

    public IRenderable? Footer
    {
        get => _footer;
        set => _footer = value;
    }

    internal void Clear()
    {
        _header = null;
        _logs.Clear();
        _footer = null;
    }
}
