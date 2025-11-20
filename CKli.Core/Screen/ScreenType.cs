using CK.Core;
using System;
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
    IRenderable? _errorHead;
    IRenderable? _warningHead;
    readonly bool _canBeInteractive;
    readonly bool _hasAnsiLink;

    IRenderable ErrorHead => _errorHead ??= Text( "Error:" )
                                            .Box( paddingLeft: 1,
                                                  paddingRight: 1,
                                                  style: new TextStyle( new Color( ConsoleColor.Black, ConsoleColor.DarkRed ), TextEffect.Bold ) );

    IRenderable WarningHead => _warningHead ??= Text( "Warning:" )
                                                .Box( paddingLeft: 1,
                                                      paddingRight: 1,
                                                      style: new TextStyle( new Color( ConsoleColor.Yellow, ConsoleColor.Black ), TextEffect.Bold ) );

    /// <summary>
    /// Gets a passive, basic screen type. Applies to <see cref="StringScreen"/> and <see cref="NoScreen"/>.
    /// </summary>
    public static readonly ScreenType Default = new ScreenType( false, false );

    /// <summary>
    /// ActivityMonitor tag that can be used to display a persistent info, trace or debug on the <see cref="IScreen"/>.
    /// <para>
    /// Fatal, Errors and Warnings are always persistent.
    /// </para>
    /// </summary>
    public static readonly CKTrait CKliScreenTag = ActivityMonitor.Tags.Register( "Screen" );

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
    /// Creates a mono or multi line block of text.
    /// </summary>
    /// <param name="text">The raw text.</param>
    /// <param name="style">Optional text style.</param>
    /// <returns>The renderable.</returns>
    [return: NotNullIfNotNull( nameof( text ) )]
    public TextBlock? Text( string? text, TextStyle style = default ) => TextBlock.FromText( this, text, style );

    /// <summary>
    /// Creates a mono or multi line block of text.
    /// </summary>
    /// <param name="text">The raw text.</param>
    /// <param name="color">Foreground and background text color.</param>
    /// <param name="effect">Optional effect.</param>
    /// <returns>The renderable.</returns>
    [return: NotNullIfNotNull( nameof( text ) )]
    public TextBlock? Text( string? text, Color color, TextEffect effect = TextEffect.Ignore ) => TextBlock.FromText( this, text, color, effect );

    /// <summary>
    /// Creates a mono or multi line block of text with no color (<see cref="TextStyle.IgnoreColor"/> is true).
    /// </summary>
    /// <param name="text">The raw text.</param>
    /// <param name="effect">The effect.</param>
    /// <returns>The renderable.</returns>
    [return: NotNullIfNotNull( nameof( text ) )]
    public TextBlock? Text( string? text, TextEffect effect ) => TextBlock.FromText( this, text, effect );

    /// <summary>
    /// Creates a mono or multi line block of text.
    /// </summary>
    /// <param name="text">The raw text.</param>
    /// <param name="foreColor">Foreground text color.</param>
    /// <param name="backColor">Optional background text color.</param>
    /// <param name="effect">Optional effect.</param>
    /// <returns>The renderable.</returns>
    [return: NotNullIfNotNull( nameof( text ) )]
    public TextBlock? Text( string? text,
                            ConsoleColor foreColor,
                            ConsoleColor backColor = ConsoleColor.Black,
                            TextEffect effect = TextEffect.Ignore ) => TextBlock.FromText( this, text, foreColor, backColor, effect );

    /// <summary>
    /// Creates a renderable log.
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <param name="message">The log message.</param>
    /// <returns>The renderable.</returns>
    public IRenderable CreateLog( LogLevel level, string message )
    {
        if( level >= LogLevel.Warn )
        {
            var h = level == LogLevel.Warn ? WarningHead : ErrorHead;
            return h.AddRight( Text( message, TextStyle.Default ) );
        }
        return Text( message, TextStyle.Default );
    }


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

        public IRenderable SetWidth( int width, bool allowWider ) => this;

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

        private protected override void BuildSegmentTree( int line, SegmentRenderer parent ) { }

        public override IRenderable SetWidth( int width, bool allowWider ) => this;
    }

}
