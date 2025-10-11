using CK.Core;
using CKli.Core;
using System;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPlugin : Command
{
    public CKliPlugin()
        : base( null,
                "plugin",
                "Handles CKli plugins compilation mode and provides informations.",
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
        PluginCompilationMode? compileMode = default;
        if( sCompileMode != null )
        {
            if( !Enum.TryParse<PluginCompilationMode>( sCompileMode, ignoreCase: true, out var mode ) )
            {
                monitor.Error( $"Invalid '--compile-mode'. Must be None, Debug or Release." );
            }
            compileMode = mode;
        }
        return ValueTask.FromResult( cmdLine.CheckNoRemainingArguments( monitor )
                                     && Plugin( monitor, context, compileMode ) );
    }

    static bool Plugin( IActivityMonitor monitor,
                        CKliEnv context,
                        PluginCompilationMode? compileMode )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            if( compileMode.HasValue
                && compileMode.Value != world.DefinitionFile.CompilationMode
                && !world.SetPluginCompilationMode( monitor, compileMode.Value ) )
            {
                return false;
            }
            bool success = world.RaisePluginInfo( monitor, out var headerText, out var infos );
            context.Screen.DisplayPluginInfo( headerText, infos );
            return success;
        }
        finally
        {
            stack.Dispose();
        }
    }

}
