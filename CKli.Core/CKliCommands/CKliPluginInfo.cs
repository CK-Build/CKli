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
                "Handles CKli plugins compilation mode and provides information.",
                arguments: [],
                options: [(["--compile-mode"],
                            """
                            Sets the compilation mode. Can be:
                            - Release: (Default) plugins are compiled in Release mode.
                            - Debug: Plugins are compiled in Debug mode.
                            - None: Plugins are not compiled (uses reflection).
                            """,
                            Multiple: false)],
                flags: [] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string? sCompileMode = cmdLine.EatSingleOption( "--compile-mode" );
        PluginCompileMode? compileMode = default;
        if( sCompileMode != null )
        {
            if( !Enum.TryParse<PluginCompileMode>( sCompileMode, ignoreCase: true, out var mode ) )
            {
                monitor.Error( $"Invalid '--compile-mode'. Must be None, Debug or Release." );
                return ValueTask.FromResult( false );
            }
            compileMode = mode;
        }
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && PluginInfo( monitor, this, context, compileMode ) );
    }

    static bool PluginInfo( IActivityMonitor monitor,
                            Command command,
                            CKliEnv context,
                            PluginCompileMode? compileMode )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command );
            if( compileMode.HasValue
                && compileMode.Value != world.DefinitionFile.CompileMode
                && !world.SetPluginCompileMode( monitor, compileMode.Value ) )
            {
                return false;
            }
            bool success = world.RaisePluginInfo( monitor, out var headerText, out var infos );
            context.Screen.DisplayPluginInfo( headerText, infos );
            // Consider that the final result requires no error when saving a dirty World's DefinitionFile.
            return stack.Close( monitor );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }

}
