using System;

namespace CKli.Core;

public interface IRenderTarget
{
    void Append( ReadOnlySpan<char> text, TextStyle style );

    internal void EndOfLine();
}
