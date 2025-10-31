using CK.Core;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Core;

/// <summary>
/// Models screen capabilities. This is also a generator for <see cref="IRenderable"/> objects
/// thanks to <see cref="Unit"/> and <see cref="EmptyString"/>.
/// </summary>
public sealed class ScreenType
{
    readonly IRenderable _unit;
    TextBlock? _emptyString;
    readonly bool _canBeInteractive;
    readonly bool _hasAnsiLink;

    /// <summary>
    /// Gets a passive, basic screen type. Applies to <see cref="StringScreen"/> and <see cref="NoScreen"/>.
    /// </summary>
    public static readonly ScreenType Default = new ScreenType( false, false );

    /// <summary>
    /// Initializes a new screen type.
    /// </summary>
    /// <param name="canBeInteractive">True if the screen can be in interactive mode.</param>
    /// <param name="hasAnsiLink">True if screen supports Ansi links.</param>
    public ScreenType( bool canBeInteractive, bool hasAnsiLink )
    {
        _canBeInteractive = canBeInteractive;
        _hasAnsiLink = hasAnsiLink;
        _unit = new RenderableUnit( this );
    }

    /// <summary>
    /// Gets whether the screen can be in interactive mode.
    /// </summary>
    public bool CanBeInteractive => _canBeInteractive;

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

        public int NominalWidth => 0;

        public ScreenType ScreenType { get; }

        public IRenderable SetWidth( int width ) => this;

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

        public override IRenderable SetWidth( int width ) => this;
    }

}
