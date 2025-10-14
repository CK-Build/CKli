using System;

namespace CKli.Core;

public interface IRenderTarget
{
    void Append( ReadOnlySpan<char> text, TextStyle style );
    void BeginUpdate();
    void EndUpdate();
    internal void EndOfLine();
}
