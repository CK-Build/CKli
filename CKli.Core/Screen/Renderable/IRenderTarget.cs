using System;

namespace CKli.Core;

public interface IRenderTarget
{
    void Write( ReadOnlySpan<char> text, TextStyle style );
    void BeginUpdate();
    void EndUpdate();
    internal void EndOfLine();
}
