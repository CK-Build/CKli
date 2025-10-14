using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPluginRemove : Command
{
    public CKliPluginRemove()
        : base( null,
                "plugin remove",
                "Fully removes a plugin from the current World. It must not have dependent plugins otherwise this fails.",
                [("pluginName", """The plugin name "MyPlugin" (or "CKli.MyPlugin.Plugin") to remove.""")],
                [],
                [
                    (["--allow-lts"], "Allows the current world to be a Long Term Support world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string pluginName = cmdLine.EatArgument();
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && CKliPluginCreate.CreateOrRemovePlugin( monitor, context, pluginName, allowLTS, create: false ) );
    }

}
