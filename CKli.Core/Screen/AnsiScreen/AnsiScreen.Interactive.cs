using CK.Core;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed partial class Interactive : IInteractiveScreen
    {
        readonly AnsiScreen _screen;
        readonly HeaderDisplay _header;
        readonly BodyDisplay _body;
        readonly FooterDisplay _footer;
        readonly Prompt _prompt;

        public Interactive( AnsiScreen screen )
        {
            _screen = screen;
            _header = new HeaderDisplay( this );
            _body = new BodyDisplay( this );
            _footer = new FooterDisplay( this );
            _prompt = new Prompt( this );
        }

        public ScreenType ScreenType => _screenType;

        public int Width => _screen.Width;

        public void Display( IRenderable renderable ) => _body.Add( renderable );

        public void OnLog( LogLevel level, string text, bool isOpenGroup = false ) => _header.AddLog( _screen.CreateLog( level, text ) );

        void IScreen.Close() => ((IScreen)_screen).Close();

        void IScreen.OnLogOther( LogLevel level, string? text, bool isOpenGroup ) => ((IScreen)_screen).OnLogOther( level, text, isOpenGroup );

        IInteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor ) => throw new NotSupportedException();

        public override string ToString() => string.Empty;

        public Task<CommandLineArguments?> PromptAsync( IActivityMonitor monitor, CKliEnv context )
        {
            _screen._width = GetWindowWidth();
            _screen._animation.Hide();
            // TraceMode: Let the current screen go, restart a new one.
            // RedrawMode: Simply clear the current screen before by erasing previous lines.
            // Currently, only TraceMode here...
            _header.Render();
            _body.Render();
            _footer.Render();
            _prompt.Render( context );
            var line = Console.ReadLine();
            // An interactive command should be able to keep the current body and footer
            // and modify them (transform visitor). This is for "local", "private", "sub" interactive commands
            // that are subordinated to a previous command.
            // Should they handle the Prompt directly? By entering a sub "Console.ReadLine" loop?
            // It seems to be the most powerful way to go. A command would be able to fully control the
            // header/body/footer screen (handling mouse events) and specific prompts...
            // Currently, we reset everything.
            _header.Reset();
            _body.Reset();
            _footer.Reset();
            return Task.FromResult( CreateLine( line ) );
        }

        static CommandLineArguments? CreateLine( string? line )
        {
            if( line == null ) return null;
            if( line.StartsWith( "ckli", StringComparison.OrdinalIgnoreCase ) )
            {
                line = line.Substring( 4 );
            }
            if( line.Equals( "exit", StringComparison.OrdinalIgnoreCase ) )
            {
                return null;
            }
            return new CommandLineArguments( line );
        }
    }
}
