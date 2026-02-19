using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Removes a plugin.
/// This reloads the plugins (and depending on the <see cref="WorldDefinitionFile.CompileMode"/> recompiles them).
/// <para>
/// This command is public: primary plugin constructors and their <see cref="PluginBase.Initialize(IActivityMonitor)"/> method
/// can observe a non null <see cref="PrimaryPluginContext.Command"/> during the reload steps.
/// </para>
/// </summary>
public sealed class CKliPluginRemove : Command
{
    internal CKliPluginRemove()
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

    /// <inheritdoc />
    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string pluginName = cmdLine.EatArgument();
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && CKliPluginCreate.CreateOrRemovePlugin( monitor, this, context, pluginName, allowLTS, create: false ) );
    }

}
