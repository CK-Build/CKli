using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPluginCreate : CommandDescription
{
    public CKliPluginCreate()
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
                                                                    CommandCommonContext context,
                                                                    CommandLineArguments cmdLine )
    {
        string pluginName = cmdLine.EatArgument();
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        return ValueTask.FromResult( cmdLine.CheckNoRemainingArguments( monitor )
                                     && CreateOrRemovePlugin( monitor, context, pluginName, allowLTS, create: true ) );
    }

    internal static bool CreateOrRemovePlugin( IActivityMonitor monitor, CommandCommonContext context, string pluginName, bool allowLTS, bool create )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            if( world.DefinitionFile.IsPluginsDisabled )
            {
                return RequiresEnabledPlugins( monitor );
            }
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return CKliRepoAdd.RequiresAllowLTS( monitor, world.Name );
            }
            return create
                    ? world.CreatePlugin( monitor, pluginName )
                    : world.RemovePlugin( monitor, pluginName );
        }
        finally
        {
            stack.Dispose();
        }
    }

    internal static bool RequiresEnabledPlugins( IActivityMonitor monitor )
    {
        monitor.Error( $"Plugins are disabled for this world." );
        return false;
    }


}
