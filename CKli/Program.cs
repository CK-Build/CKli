using CK.Core;
using CKli;
using CKli.Core;
using ConsoleAppFramework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

ConsoleApp.Version = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() ).ToString();

var arguments = new CommandLineArguments( args );

if( arguments.EatFlag( "--launch-debugger" ) )
{
    if( !Debugger.IsAttached ) Debugger.Launch();
}

CKliRootEnv.Initialize();

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

if( arguments.EatFlag( "--version" ) )
{
    var info = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() );
    Console.WriteLine( $"CKli - {info.Version} - {info.OriginalInformationalVersion}." );
}

World.PluginLoader = CKli.Loader.PluginLoadContext.Load;
CKliRunEnv.Initialize( Environment.CurrentDirectory );


var argsList = args.ToList();

HandleLaunchDebugger( argsList );

var explicitPath = HandlePathArgument( argsList );
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
bool hasHelp = HandleHelp( argsList );

var gitStackPath = StackRepository.FindGitStackPath( explicitPath );
if( gitStackPath.IsEmptyPath )
{
    Console.WriteLine( explicitPath != null
                        ? $"Unable to find a stack repository from path '{explicitPath}'."
                        : "Unable to find a stack repository from current directory." );
    if( hasHelp ) DisplayHelp( [] );
    return;
}
if( hasHelp )
{

}

CommandContext.LogFilter = HandleVerbosity( argsList );


var app = ConsoleApp.Create();
await app.RunAsync( argsList.ToArray() );

static void DisplayHelp( IEnumerable<CommandDescription> commands )
{
    Console.WriteLine( """

        Global options:

            -p, --path:      Set the working path. This overrides the current directory.
            --debug-launch:  Launch a debugger when starting.            
        """ );
}


static bool HandleHelp( List<string> args ) => args.Remove( "--help" ) | args.Remove( "-h" ) | args.Remove( "-?" );

static LogFilter HandleVerbosity( List<string> args )
{
    LogFilter log = new LogFilter( LogLevelFilter.Info, LogLevelFilter.Info );
    int idxV = args.IndexOf( "-v" );
    if( idxV < 0 ) idxV = args.IndexOf( "--verbosity" );
    if( idxV >= 0 )
    {
        args.RemoveAt( idxV );
        bool handled = true;
        if( idxV < args.Count )
        {
            var s = args[idxV].ToUpperInvariant();
            switch( s )
            {
                case "Q":
                case "QUIET":
                    log = LogFilter.Quiet;
                    break;
                case "M":
                case "MINIMAL":
                    log = LogFilter.Minimal;
                    break;
                case "N":
                case "NORMAL":
                    break;
                case "D":
                case "DETAILED":
                    log = LogFilter.Diagnostic;
                    break;
                case "DIAG":
                case "DIAGNOSTIC":
                    log = LogFilter.Detailed;
                    break;
                default:
                    handled = false;
                    break;
            }
        }
        if( handled )
        {
            args.RemoveAt( idxV );
        }
        else
        {
            Console.WriteLine( "Missing verbosity level. Using Diagnostic." );
            log = LogFilter.Diagnostic;
        }
    }
    return log;
}

static void HandleLaunchDebugger( List<string> args )
{
    int idxDebug = args.IndexOf( "--debug-launch" );
    if( idxDebug >= 0 )
    {
        args.RemoveAt( idxDebug );
        if( !Debugger.IsAttached ) Debugger.Launch();
    }
}

static string? HandlePathArgument( List<string> args )
{
    string? path = null;
    int idxPath = args.IndexOf( "-p" );
    if( idxPath < 0 ) idxPath = args.IndexOf( "--path" );
    if( idxPath >= 0 )
    {
        args.RemoveAt( idxPath );
        if( idxPath < args.Count )
        {
            path = args[idxPath];
            args.RemoveAt( idxPath );
        }
    }
    return path;
}
