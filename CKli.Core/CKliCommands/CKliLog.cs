using CK.Core;
using CliWrap;
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
    const string _successfulLogMarker = "SUCCESSFUL-LOG-EXECUTION";
    static ReadOnlySpan<byte> _successfulLogMarkerBytes => "SUCCESSFUL-LOG-EXECUTION"u8;
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

        bool success = RemoveStupidLogFile( monitor, out string textFolder, out string? firstLogFilePath );
        try
        {
            if( folder )
            {
                success &= OpenLogFolder( monitor, textFolder );
            }
            else
            {
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
        if( success )
        {
            monitor.UnfilteredLog( LogLevel.Info | LogLevel.IsFiltered, null, _successfulLogMarker, null );
        }
        return ValueTask.FromResult( success );
    }

    internal static bool RemoveStupidLogFile( IActivityMonitor monitor )
    {
        return RemoveStupidLogFile( monitor, out _, out _ );
    }

    static bool RemoveStupidLogFile( IActivityMonitor monitor, out string textFolder, out string? firstLogFilePath )
    {
        textFolder = LogFile.RootLogPath + "Text/";
        firstLogFilePath = null;
        byte[] filterBuffer = ArrayPool<byte>.Shared.Rent( 8192 );
        bool success = true;
        try
        {
            foreach( var f in Directory.EnumerateFiles( textFolder, "*.log" ).OrderDescending() )
            {
                if( HasSuccessfulCKliLogMarker( monitor, f, filterBuffer ) )
                {
                    success &= FileHelper.DeleteFile( monitor, f );
                }
                else
                {
                    firstLogFilePath = f;
                    break;
                }
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"While discovering logs in '{textFolder}'.", ex );
            success = false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return( filterBuffer );
        }
        return success;
    }

    static bool HasSuccessfulCKliLogMarker( IActivityMonitor monitor, string path, byte[] buffer )
    {
        try
        {
            using Microsoft.Win32.SafeHandles.SafeFileHandle handle = File.OpenHandle( path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan );
            int count = RandomAccess.Read( handle, buffer, 0 );
            return buffer.AsSpan( 0, count ).IndexOf( _successfulLogMarkerBytes ) > 0;
        }
        catch( Exception ex ) 
        {
            monitor.Error( $"While filtering out '{_successfulLogMarker}' log file '{path}'.", ex );
            return false;
        }
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
