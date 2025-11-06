using CK.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core;

public sealed partial class InteractiveScreen : IScreen
{
    readonly IScreen _screen;
    readonly IRenderTarget _target;
    readonly Driver _driver;
    readonly InteractiveHeader _header;
    readonly InteractiveBody _body;
    readonly InteractiveFooter _footer;
    readonly List<CommandLineArguments> _history;
    readonly InteractiveScreenBuilder _defaultScreenBuilder;

    IRenderable? _previousScreen;
    object? _currentScreenState;
    CKliEnv _context;
    InteractiveScreenBuilder _nextScreenBuilder;

    internal InteractiveScreen( IScreen screen,
                                CKliEnv initialContext,
                                IRenderTarget target,
                                Driver driver,
                                InteractiveScreenBuilder? defaultScreenBuilder = null )
    {
        _screen = screen;
        _target = target;
        _nextScreenBuilder = _defaultScreenBuilder = defaultScreenBuilder ?? DefaultScreenBuilder;
        _context = new CKliEnv( this, initialContext.SecretsStore, initialContext.CurrentDirectory, initialContext.CurrentStackPath );
        _driver = driver;
        _history = new List<CommandLineArguments>();
        _header = new InteractiveHeader();
        _body = new InteractiveBody();
        _footer = new InteractiveFooter( this );
    }

    /// <summary>
    /// Gets the current context.
    /// </summary>
    public CKliEnv Context => _context;

    public InteractiveHeader Header => _header;

    public InteractiveBody Body => _body;

    public InteractiveFooter Footer => _footer;

    public IReadOnlyList<CommandLineArguments> History => _history;

    public ScreenType ScreenType => _screen.ScreenType;

    public int Width => _screen.Width;

    /// <summary>
    /// Gets or sets a state object for the current screen.
    /// <para>
    /// It is up to commands that are "interactive aware" to handle this.
    /// </para>
    /// </summary>
    public object? CurrentScreenState
    {
        get => _currentScreenState;
        set => _currentScreenState = value;
    }

    /// <summary>
    /// Gets the previous screen if this is not the initial interactive state.
    /// </summary>
    public IRenderable? PreviousScreen => _previousScreen;

    /// <summary>
    /// Gets or sets the screen builder to use for the next screen.
    /// Setting it to null resets to the default builder.
    /// <para>
    /// To keep the <see cref="PreviousScreen"/> unchanged (it has been interactively updated or should not change
    /// for any reason), simply sets a <see cref="InteractiveScreenBuilder"/> that blindly returns the previous screen.
    /// </para>
    /// </summary>
    [AllowNull]
    public InteractiveScreenBuilder NextScreenBuilder
    {
        get => _nextScreenBuilder;
        set => _nextScreenBuilder = value ?? _defaultScreenBuilder;
    }

    /// <summary>
    /// Default <see cref="InteractiveScreenBuilder"/>.
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="header">The screen header.</param>
    /// <param name="body">The screen body.</param>
    /// <param name="footer">The screen footer.</param>
    /// <returns>The full screen.</returns>
    public static IRenderable DefaultScreenBuilder( ScreenType screenType,
                                                    InteractiveHeader header,
                                                    InteractiveBody body,
                                                    InteractiveFooter footer )
    {
        return screenType.Unit.AddBelow( header.Header,
                                         header.Logs,
                                         header.Footer,
                                         body.Header,
                                         body.Content,
                                         body.Footer,
                                         footer.Header,
                                         footer.Prompt );
    }

    public void Display( IRenderable renderable ) => _body.Content.Add( renderable );

    public void ScreenLog( LogLevel level, string text ) => _header.Logs.Add( _screen.ScreenType.CreateLog( level, text ) );

    void IScreen.Close() => ((IScreen)_screen).Close();

    void IScreen.OnLog( LogLevel level, string? text, bool isOpenGroup ) => ((IScreen)_screen).OnLog( level, text, isOpenGroup );

    InteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor, CKliEnv context ) => throw new System.InvalidOperationException();

    internal async Task<bool> RunInteractiveAsync( IActivityMonitor monitor, CommandLineArguments initial )
    {
        Throw.DebugAssert( _history.Count == 0 );
        _history.Add( initial );
        for(; ; )
        {
            var newScreen = _nextScreenBuilder( _screen.ScreenType, _header, _body, _footer )
                                .SetWidth( _driver.UpdateScreenWidth(), allowWider: false );
            if( newScreen != _previousScreen )
            {
                newScreen.Render( _target, newLine: false );
                _previousScreen = newScreen;
            }
            _header.Clear();
            _body.Clear();
            _footer.Clear();
            CommandLineArguments? cmd = await _driver.PromptAsync( monitor );
            if( cmd == null ) break;
            _history.Add( cmd );
            await CKliCommands.HandleCommandAsync( monitor, _context, cmd );
        }
        return true;
    }

    public override string ToString() => string.Empty;

}
