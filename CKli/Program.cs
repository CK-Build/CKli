using CK.Core;
using CKli;
using CKli.Core;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;

var arguments = new CommandLineArguments( args );

if( arguments.EatFlag( "--debug-launch" ) )
{
    if( !Debugger.IsAttached ) Debugger.Launch();
}

// Sets the Environment.CurrentDirectory before CKliRootEnv.Initialize().
var explicitPath = arguments.EatSingleOption( "--path", "-p" );
if( explicitPath != null )
{
    if( Directory.Exists( explicitPath ) )
    {
        Environment.CurrentDirectory = explicitPath;
    }
    else
    {
        Console.WriteLine( $"Invalid provided path. Directory '{explicitPath}' doesn't exist." );
        Environment.ExitCode = -1;
        return;
    }
}
// Since we Console.WriteLine we dont' need the environment to be setup.
if( arguments.EatFlag( "--version" ) )
{
    var info = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() );
    Console.WriteLine( $"CKli - {info.Version} - {info.OriginalInformationalVersion}." );
}
// Initializes the root environment.
World.PluginLoader = CKli.Loader.PluginLoadContext.Load;
CKliRootEnv.Initialize( arguments: arguments );
CKliRootEnv.GlobalOptions = GetGlobalOptions;
CKliRootEnv.GlobalFlags = GetGlobalFlags;

var monitor = new ActivityMonitor();
monitor.Output.RegisterClient( new ScreenLogger( CKliRootEnv.Screen ) );
monitor.Info( $"Executing '{arguments.InitialArguments.Concatenate( " " )}'." );

CoreApplicationIdentity.Initialize();

Environment.ExitCode = (await CKliCommands.HandleCommandAsync( monitor, CKliRootEnv.DefaultCKliEnv, arguments ))
                        ? 0
                        : -1;

await CKliRootEnv.CloseAsync( monitor, arguments );

static ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> GetGlobalOptions()
{
    return [(["--path", "-p"], "Sets the working path. This overrides the current directory.", Multiple:false)];
}

static ImmutableArray<(ImmutableArray<string> Names, string Description)> GetGlobalFlags() => [
        (["--version"], "Displays this CKli version."),
        (["--screen"], """
                        Change the screen display. Can be:
                        - none: No display at all.
                        - no-color (or no_color): Basic display, no animation.
                        - force-ansi: Always consider an Ansi terminal.

                        If a non empty "NO_COLOR" exists in the environment variables, it is honored.
                        See https://no-color.org/.
                        Any other values are ignored: the default detection is applied.
             """),
        (["--debug-launch"], "Launches a debugger when starting.")
    ];


