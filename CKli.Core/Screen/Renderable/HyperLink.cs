using CK.Core;
using System;

namespace CKli.Core;

public sealed class HyperLink : IRenderable
{
    readonly ScreenType _screenType;
    readonly string _text;
    readonly string _link;

    public HyperLink( ScreenType screenType, string text, string link )
    {
        CheckLinkOrText( text );
        CheckLinkOrText( link );
        _screenType = screenType;
        _text = text;
        _link = link;
    }

    static void CheckLinkOrText( string s )
    {
        var linkOrText = s.AsSpan();
        Throw.CheckArgument( linkOrText.Length > 0 && linkOrText.Trim().Length == s.Length );
    }

    public HyperLink( ScreenType screenType, string url )
        : this( screenType, url, url )
    {
    }

    public ScreenType ScreenType => _screenType;

    public int Width => throw new NotImplementedException();

    public int Height => 1;

    public bool HasText => !ReferenceEquals( _text, _link );

    public string Text => _text;

    public string Link => _link;

    public IRenderable Accept( RenderableVisitor visitor ) => visitor.Visit( this );

    public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight )
    {
        throw new NotImplementedException();
    }
}

