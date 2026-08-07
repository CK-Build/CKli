using CK.Core;
using CKli.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPluginDisable : Command
{
    public CKliPluginDisable()
        : base( null,
                "plugin disable",
                """Disables a plugin.""",
                [("name", """Plugin name to disable.""")],
                [], [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        var name = cmdLine.EatArgument();
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && CKliPluginEnable.EnableOrDisablePlugin( monitor, context, name, enable: false ) ); 
    }
}
