using CK.Core;
using CKli;
using CKli.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

var arguments = new CommandLineArguments( args );

if( arguments.HasCKliDebugFlag )
{
    Throw.DebugAssert( "We don't want to break in completion request: CommandLineArguments does that.", !arguments.IsCompletionRequest );
    Debugger.Launch();
}

// Sets the Environment.CurrentDirectory before CKliRootEnv.Initialize() and before completion:
// if the --path is specified, the completion must "in the path".
var explicitPath = arguments.ExplicitPathOption;
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
// Now that the --path has been set, we can handle the completion request.
if( arguments.IsCompletionRequest )
{
    CKliCommands.HandleCompletion( arguments.InitialArguments.AsSpan() );
    Environment.ExitCode = 0;
    return;
}

// Since we Console.WriteLine we don't need the environment to be setup.
if( arguments.HasVersionFlag )
{
    var info = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() );
    Console.WriteLine( $"CKli - {info.Version} - {info.OriginalInformationalVersion}." );
    return;
}
// Initializes the root environment.
World.PluginLoader = CKli.Loader.PluginLoadContext.Load;
CKliRootEnv.Initialize( arguments: arguments );

var monitor = new ActivityMonitor();
monitor.Output.RegisterClient( new ScreenLogger( monitor, CKliRootEnv.Screen, CK.Monitoring.GrandOutput.Default ) );

CoreApplicationIdentity.Initialize();

Environment.ExitCode = (await CKliCommands.HandleCommandAsync( monitor, CKliRootEnv.DefaultCKliEnv, arguments ).ConfigureAwait( false ))
                        ? 0
                        : -1;

await CKliRootEnv.CloseAsync( monitor, arguments ).ConfigureAwait( false );

