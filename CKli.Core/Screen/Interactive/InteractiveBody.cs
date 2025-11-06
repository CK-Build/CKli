using System.Collections.Generic;

namespace CKli.Core;

public sealed class InteractiveBody
{
    IRenderable? _header;
    readonly List<IRenderable> _content;
    IRenderable? _footer;

    internal InteractiveBody()
    {
        _content = new List<IRenderable>();
    }

    public List<IRenderable> Content => _content;

    public IRenderable? Header
    {
        get => _header;
        set => _header = value;
    }

    public IRenderable? Footer
    {
        get => _footer;
        set => _footer = value;
    }
}
