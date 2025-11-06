using CK.Core;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core;

public sealed partial class InteractiveScreen
{
    internal abstract class Driver
    {
        readonly InteractiveScreen _screen;
        readonly IRenderTarget _target;

        protected Driver( IScreen screen, CKliEnv initialContext, IRenderTarget target )
        {
            _screen = new InteractiveScreen( screen, initialContext, target, this );
            _target = target;
        }

        public InteractiveScreen InteractiveScreen => _screen;

        internal protected abstract int UpdateScreenWidth();

        public virtual Task<CommandLineArguments?> PromptAsync( IActivityMonitor monitor )
        {
            var line = Console.ReadLine();
            return Task.FromResult( CreateCommandLineArguments( line ) );
        }

        protected virtual CommandLineArguments? CreateCommandLineArguments( string? line )
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
            if( line == "debug" || line == "debug-launch" || line == "-debug-launch" )
            {
                if( !Debugger.IsAttached ) Debugger.Launch();
            }
            return new CommandLineArguments( line );
        }

    }

}
