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


var argsList = args.ToList();
var (logFilter, launchDebugger, hasHelp, path) = HandleGlobalOptions( argsList );

CommandContext.LogFilter = logFilter;

if( launchDebugger && !Debugger.IsAttached ) Debugger.Launch();

if( path != null )
{
    if( Directory.Exists( path ) ) Environment.CurrentDirectory = path;
    else
    {
        Console.WriteLine( $"Invalid provided path. Directory '{path}' doesn't exist." );
        Environment.ExitCode = -1;
        return;
    }
}

World.PluginLoader = CKli.Loader.PluginLoadContext.Load;

var app = ConsoleApp.Create();

app.Run( argsList.ToArray() );
if( hasHelp )
{
    Console.WriteLine( """

        Global options:

            -v, --vervosity: Set log verbosity
                                 Q[uiet]       Error groups and line only.
                                 M[inimal]     Information groups and Warning lines.
                                 N[ormal]      Information groups and Information lines.
                                 D[etailed]    Trace groups and lines.
                                 Diag[nostic]  Debug groups and lines.
            
            -p, --path:      Set the working path. This overrides the current directory.
        
            --debug-launch:  Launch a debugger when starting.            
        """ );
}

static (LogFilter LogFilter, bool LaunchDebugger, bool HasHelp, string? Path) HandleGlobalOptions( List<string> args )
{
    LogFilter log = HandleVerbosity( args );
    bool launchDebugger = HandleLauchDebugger( args );
    string? path = HandlePath( args );

    return (log,
            launchDebugger,
            args.IndexOf( "--help" ) + args.IndexOf( "-h" ) != -2,
            path);

    static LogFilter HandleVerbosity( List<string> args )
    {
        // Waiting for CK-ActivityMonitor > v25.0.0.
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

    static bool HandleLauchDebugger( List<string> args )
    {
        bool launchDebugger = false;
        int idxDebug = args.IndexOf( "--debug-launch" );
        if( idxDebug >= 0 )
        {
            launchDebugger = true;
            args.RemoveAt( idxDebug );
        }

        return launchDebugger;
    }

    static string? HandlePath( List<string> args )
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
}
