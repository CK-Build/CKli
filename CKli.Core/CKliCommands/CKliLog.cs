using CK.Core;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Logs command.
/// </summary>
sealed class CKliLog : Command
{
    static string? _fileOpenerPath;

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
        using var p = Process.Start( new ProcessStartInfo()
        {
            FileName = folder,
            UseShellExecute = true,
            Verb = "open"
        } );
        return true;
    }

    static bool OpenLogFile( IActivityMonitor monitor, string path )
    {
        if( _fileOpenerPath == null )
        {
            //
            // This prevents the associated program to write its text to our console.
            // For instance, VSCode pollutes the console with lines like:
            // "[main 2025-11-04T08:27:57.303Z] update#setState idle"
            //
            _fileOpenerPath = Path.Combine( CKliRootEnv.AppLocalDataPath, "CmdOpenFile.bat" );
            if( !File.Exists( _fileOpenerPath ) )
            {
                using( CKliRootEnv.AcquireAppMutex( monitor ) )
                {
                    File.WriteAllText( _fileOpenerPath, "start \"\" %1" );
                }
            }
        }
        using var p = Process.Start( new ProcessStartInfo()
        {
            FileName = _fileOpenerPath,
            Arguments = '"' + path + '"',
            CreateNoWindow = true,
        } );
        if( p == null )
        {
            monitor.Error( $"Unable to open log file '{path}'." );
            return false;
        }
        return true;
    }
}
