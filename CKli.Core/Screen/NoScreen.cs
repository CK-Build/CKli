using CK.Core;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// A screen that renders nothing.
/// </summary>
public sealed class NoScreen : IScreen
{
    readonly NoScreenType _screenType;

    public NoScreen()
    {
        _screenType = new NoScreenType();
    }

    public ScreenType ScreenType => _screenType;

    public void Clear() {}

    public void Display( IRenderable renderable ) { }

    public int Width => IScreen.MaxScreenWidth;

    public void OnLogErrorOrWarning( LogLevel level, string message, bool isOpenGroup ) { }

    void IScreen.OnLogOther( LogLevel level, string? text, bool isOpenGroup ) { }

    void IScreen.Close()
    {
    }

    public override string ToString() => string.Empty;

    sealed class NoScreenType : ScreenType
    {
        public NoScreenType()
            : base( false, false )
        {
        }
    }


}
