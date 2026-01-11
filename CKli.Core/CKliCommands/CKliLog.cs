using CK.Core;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Logs command.
/// </summary>
sealed class CKliLog : Command
{

    internal CKliLog()
        : base( null,
                "log",
                "Opens the log file of the last run.",
                [],
                [],
                flags: [(["--folder","-f"], "Open the folder that contains the log files instead of the last log.")] )
    {
    }

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        var folder = cmdLine.EatFlag( Flags[0].Names );
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        Throw.DebugAssert( LogFile.RootLogPath != null && LogFile.RootLogPath[^1] == Path.DirectorySeparatorChar );

        bool success = true;
        try
        {
            var textFolder = LogFile.RootLogPath + "Text/";
            if( folder )
            {
                success &= OpenLogFolder( monitor, textFolder );
            }
            else
            {
                var firstLogFilePath = Directory.EnumerateFiles( textFolder, "*.log" ).OrderDescending().FirstOrDefault();
                if( firstLogFilePath != null )
                {
                    success &= OpenLogFile( monitor, firstLogFilePath );
                }
                else
                {
                    monitor.Warn( $"No log file found in folder '{textFolder}'." );
                }
            }
        }
        catch( Exception ex )
        {
            monitor.Error( "While opening logs.", ex );
            success = false;
        }
        return ValueTask.FromResult( success );
    }

    static bool OpenLogFolder( IActivityMonitor monitor, string folder )
    {
        return PlatformHelper.OpenFolder( monitor, folder );
    }

    static bool OpenLogFile( IActivityMonitor monitor, string path )
    {
        return PlatformHelper.OpenFile( monitor, path );
    }
}
