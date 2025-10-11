using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPluginDisable : Command
{
    public CKliPluginDisable()
        : base( null,
                "plugin disable",
                """Disables a plugin or all of them if <name> is "global".""",
                [("name", """Plugin name to disable or "global".""")],
                [], [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        var name = cmdLine.EatArgument();
        return ValueTask.FromResult( cmdLine.CheckNoRemainingArguments( monitor )
                                     && CKliPluginEnable.EnableOrDisablePlugin( monitor, context, name, enable: false ) ); 
    }
}
