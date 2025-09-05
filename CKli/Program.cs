using CK.Core;
using CKli;
using ConsoleAppFramework;
using System;
using System.Diagnostics;

ConsoleApp.Version = CSemVer.InformationalVersion.ReadFromAssembly( System.Reflection.Assembly.GetExecutingAssembly() ).ToString();

var (logFilter, launchDebugger, hasHelp) = HandleGlobalOptions( args );
CommandContext.LogFilter = logFilter;
if( launchDebugger && !Debugger.IsAttached )
{
    Debugger.Launch();
}

var app = ConsoleApp.Create();
app.Add<RootCommands>();

app.Run( args );
if( hasHelp )
{
    Console.WriteLine( """

        Global options:

            -v, --vervosity: Set log verbosity
                                 Q[uiet]       Error groups and line only.
                                 M[inimal]     Information groups and Warning lines.
                                 N[ormal]      Trace groups and Warning lines.
                                 D[etailed]    Trace groups and lines.
                                 Diag[nostic]  Debug groups and lines.
        
            --debug-launch:   Launch a debugger when starting.            
        """ );
}

static (LogFilter LogFilter, bool LaunchDebugger, bool HasHelp) HandleGlobalOptions( string[] args )
{
    LogFilter log = LogFilter.Normal;
    int idxV = Array.IndexOf( args, "--verbosity" );
    if( idxV < 0 ) idxV = Array.IndexOf( args, "-v" );
    if( idxV >= 0 )
    {
        if( ++idxV == args.Length )
        {
            Console.WriteLine( "Missing verbosity level." );
            log = LogFilter.Diagnostic;
        }
        else
        {
            var s = args[idxV].ToUpperInvariant();
            switch( s[0] )
            {
                case 'Q':
                    log = LogFilter.Quiet;
                    break;
                case 'M':
                    log = LogFilter.Minimal;
                    break;
                case 'N':
                    log = LogFilter.Normal;
                    break;
                case 'D':
                    if( s.Length > 1 && s[1] == 'I' )
                    {
                        log = LogFilter.Diagnostic;
                    }
                    else
                    {
                        log = LogFilter.Detailed;
                    }
                    break;
            }
        }
    }
    return (log,
            Array.IndexOf( args, "--debug-launch" ) >= 0,
            Array.IndexOf( args, "--help" ) + Array.IndexOf( args, "-h" ) != -2 );
}
