using CK.Core;
using System;
using System.Threading.Tasks;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed class Interactive : IInteractiveScreen
    {
        readonly AnsiScreen _screen;

        public Interactive( AnsiScreen screen )
        {
            _screen = screen;
        }

        public ScreenType ScreenType => _screenType;

        public int Width => _screen.Width;

        public void Display( IRenderable renderable ) => _screen.Display( renderable );

        public void OnLogErrorOrWarning( LogLevel level, string text, bool isOpenGroup = false ) => _screen.OnLogErrorOrWarning( level, text, isOpenGroup );

        void IScreen.Close() => ((IScreen)_screen).Close();

        void IScreen.OnLogOther( LogLevel level, string? text, bool isOpenGroup ) => ((IScreen)_screen).OnLogOther( level, text, isOpenGroup );

        IInteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor ) => throw new NotSupportedException();

        public override string ToString() => string.Empty;

        public Task<CommandLineArguments?> PromptAsync( IActivityMonitor monitor, CKliEnv context )
        {
            _screen._width = GetWindowWidth();
            _screen._animation.Hide();
            Console.Write( $"[CKli] {context.CurrentDirectory}> " );
            var line = Console.ReadLine();

            var cmdLine = line == null || line.Equals( "exit", StringComparison.OrdinalIgnoreCase )
                            ? null
                            : new CommandLineArguments( line );
            return Task.FromResult( cmdLine );
        }
    }
}
