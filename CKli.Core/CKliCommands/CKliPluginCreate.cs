using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Creates a new plugin project in the Plugins solution of the current world.
/// This reloads the plugins (and depending on the <see cref="WorldDefinitionFile.CompileMode"/> recompiles them).
/// <para>
/// This command is public: primary plugin constructors and their <see cref="PluginBase.Initialize(IActivityMonitor)"/> method
/// can observe a non null <see cref="PrimaryPluginContext.Command"/> during the reload steps.
/// </para>
/// </summary>
public sealed class CKliPluginCreate : Command
{
    internal CKliPluginCreate()
        : base( null,
                "plugin create",
                "Creates a new source based plugin project for the current World.",
                [("pluginName", """The plugin name "MyPlugin" (or "CKli.MyPlugin.Plugin") to create.""")],
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
                                     && CreateOrRemovePlugin( monitor, this, context, pluginName, allowLTS, create: true ) );
    }

    internal static bool CreateOrRemovePlugin( IActivityMonitor monitor,
                                               Command command,
                                               CKliEnv context,
                                               string pluginName,
                                               bool allowLTS,
                                               bool create )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return CKliRepoAdd.RequiresAllowLTS( monitor, world.Name );
            }
            // Both CreatePlugin and RemovePlugin handle the WorldDefinition file save and commit.
            world.SetExecutingCommand( command );
            return create
                    ? world.CreatePlugin( monitor, pluginName )
                    : world.RemovePlugin( monitor, pluginName );
        }
        finally
        {
            stack.Dispose();
        }
    }

}
