using CK.Core;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

public enum HyperLinkStyle
{
    Default = 0,

    UnderlinedText,

    UnderlinedLink,



}

public abstract class ScreenType
{
    readonly IRenderable _unit;
    TextBlock? _emptyString;
    readonly bool _isInteractive;
    readonly bool _hasAnsiLink;

    protected ScreenType( bool isInteractive, bool hasAnsiLink )
    {
        _isInteractive = isInteractive;
        _hasAnsiLink = hasAnsiLink;
        _unit = new RenderableUnit( this );
    }

    /// <summary>
    /// Gets whether the screen is in interactive mode.
    /// </summary>
    public bool IsInteractive => _isInteractive;

    /// <summary>
    /// Gets whether the screen supports Ansi link display.
    /// </summary>
    public bool HasAnsiLink => _hasAnsiLink;

    /// <summary>
    /// Gets the unit for renderables.
    /// </summary>
    public IRenderable Unit => _unit;

    /// <summary>
    /// Gets an empty block: typically used as a new line.
    /// </summary>
    public TextBlock EmptyString => _emptyString ??= new Empty( this );

    /// <summary>
    /// Shortcut to <see cref="TextBlock.FromText(ScreenType, string?, TextStyle)"/>.
    /// </summary>
    /// <param name="text">The raw text.</param>
    /// <param name="style">Optional text style.</param>
    /// <returns>The renderable.</returns>
    [return: NotNullIfNotNull( nameof( text ) )]
    public TextBlock? Text( string? text, TextStyle style = default ) => TextBlock.FromText( this, text, style );

    sealed class RenderableUnit : IRenderable
    {
        public RenderableUnit( ScreenType screenType )
        {
            ScreenType = screenType;
        }

        public int Height => 0;

        public int Width => 0;

        public int MinWidth => 0;

        public ScreenType ScreenType { get; }

        public void BuildSegmentTree( int line, SegmentRenderer parent, int actualHeight ) { }

        public IRenderable Accept( RenderableVisitor visitor ) => this;
    }

    sealed class Empty : TextBlock
    {
        public Empty( ScreenType screenType )
            : base( screenType, string.Empty, 0, TextStyle.None )
        {
        }

        public override int Height => 1;

        protected override void BuildSegmentTree( int line, SegmentRenderer parent ) { }

        public override TextBlock SetTextWidth( int width ) => this;
    }

}
