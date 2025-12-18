using CK.Core;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CKli.Core;

sealed partial class AnsiScreen
{
    sealed class Driver : InteractiveScreen.Driver
    {
        readonly AnsiScreen _screen;

        public Driver( AnsiScreen screen, CKliEnv initialContext )
            : base( screen, initialContext, screen._target )
        {
            _screen = screen;
        }

        internal protected override void OnCommandExecuted( bool success, CommandLineArguments cmdLine )
        {
            _screen._animation.Hide( false );
        }

        protected internal override void GetLogs( out int screenWidth, out VerticalContent? logs )
        {
            logs = _screen._animation.ClearLogs();
            screenWidth = _screen._animation.ScreenWidth;
        }

        protected internal override async Task<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                         CKliEnv context,
                                                                         CommandLineArguments cmd,
                                                                         List<CommandLineArguments> history )
        {
            _screen._animation.Resurrect();
            return await CKliCommands.HandleCommandAsync( monitor, context, cmd ).ConfigureAwait( false );
        }
    }

}
