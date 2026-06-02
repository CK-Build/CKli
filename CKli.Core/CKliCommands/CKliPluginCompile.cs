using CK.Core;
using CKli.Core;
using System;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Compiles and/or changes the plugins compilation mode.
/// </summary>
public sealed class CKliPluginCompile : Command
{
    internal CKliPluginCompile()
        : base( null,
                "plugin compile",
                "Compiles installed plugins and sets compilation mode.",
                arguments: [],
                options: [(["--mode"],
                            """
                            Sets the compilation mode. Can be:
                            - Release: (Default) plugins are compiled in Release mode.
                            - Debug: Plugins are compiled in Debug mode.
                            - None: Plugins are not compiled (uses reflection).
                            """,
                            Multiple: false)],
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
        string? sMode = cmdLine.EatSingleOption( "--mode" );
        bool skipPullStack = cmdLine.EatFlag( "--skip-pull-stack" );
        PluginCompileMode? compileMode = default;
        if( sMode != null )
        {
            if( !Enum.TryParse<PluginCompileMode>( sMode, ignoreCase: true, out var mode ) )
            {
                monitor.Error( $"Invalid '--mode'. Must be None, Debug or Release." );
                return ValueTask.FromResult( false );
            }
            compileMode = mode;
        }
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Compile( monitor, this, context, skipPullStack, compileMode ) );
    }

    static bool Compile( IActivityMonitor monitor,
                         Command command,
                         CKliEnv context,
                         bool skipPullStack,
                         PluginCompileMode? mode )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command );
            if( mode.HasValue && mode.Value != world.DefinitionFile.CompileMode )
            {
                if( !world.SetPluginCompileMode( monitor, mode.Value ) )
                {
                    return false;
                }
            }
            else if( !world.ForceRecompilePlugins( monitor ) )
            {
                return false;
            }
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
