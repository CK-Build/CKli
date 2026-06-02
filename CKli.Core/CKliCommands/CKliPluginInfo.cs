using CK.Core;
using CKli.Core;
using System;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Raises the <see cref="WorldEvents.PluginInfo"/> event.
/// </summary>
public sealed class CKliPluginInfo : Command
{
    internal CKliPluginInfo()
        : base( null,
                "plugin info",
                "Provides information about installed plugins.",
                arguments: [],
                options: [],
                flags: [
                    (["--skip-pull-stack"], "Don't update the stack repository.")
                    ] )
    {
    }

    /// <inheritdoc />
    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool skipPullStack = cmdLine.EatFlag( "--skip-pull-stack" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && PluginInfo( monitor, this, context, skipPullStack ) );
    }

    static bool PluginInfo( IActivityMonitor monitor,
                            Command command,
                            CKliEnv context,
                            bool skipPullStack )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command );
            bool success = world.RaisePluginInfo( monitor, out var headerText, out var infos );
            context.Screen.DisplayPluginInfo( headerText, infos );
            bool pluginLoadFailed = world.PluginsLoadFailed;
            return stack.Close( monitor ) && !pluginLoadFailed;
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }

}
