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

        public void OnLog( LogLevel level, string text, bool isOpenGroup = false ) => _screen.OnLog( level, text, isOpenGroup );

        void IScreen.Close() => ((IScreen)_screen).Close();

        void IScreen.OnLogOther( LogLevel level, string? text, bool isOpenGroup ) => ((IScreen)_screen).OnLogOther( level, text, isOpenGroup );

        IInteractiveScreen? IScreen.TryCreateInteractive( IActivityMonitor monitor ) => throw new NotSupportedException();

        public override string ToString() => string.Empty;

        public Task<CommandLineArguments?> PromptAsync( IActivityMonitor monitor, CKliEnv context )
        {
            _screen._width = GetWindowWidth();
            _screen._animation.Hide();
            DisplayPrompt( context );
            var line = Console.ReadLine();
            return Task.FromResult( CreateLine( line ) );
        }

        void DisplayPrompt( CKliEnv context )
        {
            string ctx = "CKli";
            string path = context.CurrentDirectory;
            if( !context.CurrentStackPath.IsEmptyPath )
            {
                Throw.DebugAssert( context.CurrentStackPath.LastPart == StackRepository.PublicStackName
                                    || context.CurrentStackPath.LastPart == StackRepository.PrivateStackName );
                var stackRoot = context.CurrentStackPath.RemoveLastPart();
                ctx = stackRoot.LastPart;
                int rootLen = stackRoot.Path.Length;
                path = path.Length > rootLen
                        ? context.CurrentDirectory.Path.Substring( rootLen + 1 )
                        : "";
            }
            var prompt = _screenType.Text( $"[{ctx}]", new TextStyle( ConsoleColor.Yellow ) ).Box( marginRight: 1 )
                                    .AddRight( _screenType.Text( path + '>' ) );
            _screen.Display( prompt, false );
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
