using CK.Core;
using CKli;
using CKli.Core;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
/*
 using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    // Token source to signal our main application loop to stop
    private static readonly CancellationTokenSource _cts = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("Application started. Press Ctrl+C to exit.");

        // 1. Register for SIGINT (Ctrl+C on Windows/Linux)
        using var sigIntReg = PosixSignalRegistration.Register(PosixSignal.SIGINT, HandleShutdownSignal);
        
        // 2. Register for SIGTERM (Commonly sent by Docker/Kubernetes/systemd)
        using var sigTermReg = PosixSignalRegistration.Register(PosixSignal.SIGTERM, HandleShutdownSignal);

        try
        {
            // Simulate your main application workload
            await RunApplicationLoopAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Main loop canceled by shutdown request.");
        }
        finally
        {
            // 4. Perform your cleanup tasks here
            await PerformCleanupAsync();
        }
    }

    private static void HandleShutdownSignal(PosixSignalContext context)
    {
        // Prevent the OS from instantly killing the process
        context.Cancel = true; 
        
        Console.WriteLine($"\nReceived signal: {context.Signal}. Starting graceful shutdown...");
        
        // Signal the cancellation token to stop the main workload
        _cts.Cancel();
    }

    private static async Task RunApplicationLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Console.WriteLine($"Working... {DateTime.Now:T}");
            await Task.Delay(2000, token);
        }
    }

    private static async Task PerformCleanupAsync()
    {
        Console.WriteLine("Executing cleanup tasks...");
        
        // Simulate resource flushing, database closing, or file saving
        await Task.Delay(1000); 
        
        Console.WriteLine("Cleanup completed successfully. Exiting process.");
    }
}
*/
var arguments = new CommandLineArguments( args );

if( arguments.HasCKliDebugFlag )
{
    Debugger.Launch();
}

// Sets the Environment.CurrentDirectory before CKliRootEnv.Initialize().
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
// Since we Console.WriteLine we don't need the environment to be setup.
if( arguments.HasVersionFlag )
{
    var info = InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() );
    Console.WriteLine( $"CKli - {info.Version} - {info.OriginalInformationalVersion}." );
    return;
}
// Initializes the root environment.
World.PluginLoader = CKli.Loader.PluginLoadContext.Load;
CKliRootEnv.Initialize( arguments: arguments );
CKliRootEnv.GlobalOptions = GetGlobalOptions;
CKliRootEnv.GlobalFlags = GetGlobalFlags;

var monitor = new ActivityMonitor();
monitor.Output.RegisterClient( new ScreenLogger( monitor, CKliRootEnv.Screen, CK.Monitoring.GrandOutput.Default ) );

CoreApplicationIdentity.Initialize();

Environment.ExitCode = (await CKliCommands.HandleCommandAsync( monitor, CKliRootEnv.DefaultCKliEnv, arguments ).ConfigureAwait( false ))
                        ? 0
                        : -1;

await CKliRootEnv.CloseAsync( monitor, arguments ).ConfigureAwait( false );

static ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)> GetGlobalOptions()
{
    return [(["--path", "-p"], """
        Sets the working path. This overrides the current directory.
        This must appear at the start of the command.
        When "--path" or "-p" appears after, it is an option of the command.
        """, Multiple:false)];
}

static ImmutableArray<(ImmutableArray<string> Names, string Description)> GetGlobalFlags() => [
        (["--version, -v"], """
        Displays this CKli version. 
        This flag must come first and excludes anything else.
        """),
        (["--ckli-screen"], """
                        Changes the screen display. Can be:
                        - none: No display at all.
                        - no-color (or no_color): Basic display, no animation.
                        - force-ansi: Always consider an Ansi terminal.

                        If a non empty "NO_COLOR" exists in the environment variables, it is honored.
                        See https://no-color.org/.
                        Any other values are ignored: the default detection is applied.
             """),
        (["--ckli-debug"], "Launches a debugger when starting."),
        (["--help, -?, -h, ?"], "Displays the help. This must be the last argument.")
    ];


