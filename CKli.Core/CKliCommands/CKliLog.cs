using CK.Core;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Logs command.
/// </summary>
sealed class CKliLog : Command
{
    const string _successfulLogMarker = "SUCCESSFUL-LOG-EXECUTION";
    static ReadOnlySpan<byte> _successfulLogMarkerBytes => "SUCCESSFUL-LOG-EXECUTION"u8;

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
                                                                    CommandCommonContext context,
                                                                    CommandLineArguments cmdLine )
    {
        var folder = cmdLine.EatFlag( Flags[0].Names );
        if( !cmdLine.CheckNoRemainingArguments( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        Throw.DebugAssert( LogFile.RootLogPath != null && LogFile.RootLogPath[^1] == Path.DirectorySeparatorChar );

        bool success = true;
        var textFolder = LogFile.RootLogPath + "Text/";
        try
        {
            if( folder )
            {
                success = OpenLogFolder( monitor, textFolder );
            }
            else
            {
                byte[] filterBuffer = ArrayPool<byte>.Shared.Rent( 8192 );
                try
                {
                    bool foundFile = false;
                    foreach( var f in Directory.EnumerateFiles( textFolder, "*.log" ).OrderDescending() )
                    {
                        if( HasSuccessfulCKliLogMarker( monitor, f, filterBuffer ) )
                        {
                            success |= FileHelper.DeleteFile( monitor, f );
                        }
                        else
                        {
                            foundFile = true;
                            success = OpenLogFile( monitor, f );
                            break;
                        }
                    }
                    if( !foundFile )
                    {
                        monitor.Warn( $"No log file found in folder '{textFolder}'." );
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return( filterBuffer );
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
        Process.Start( new ProcessStartInfo()
        {
            FileName = folder,
            UseShellExecute = true,
            Verb = "open"
        } );
        return true;
    }

    static bool OpenLogFile( IActivityMonitor monitor, string path )
    {
        if( Process.Start( new ProcessStartInfo()
        {
            FileName = path,
            UseShellExecute = true,
        } ) == null )
        {
            monitor.Error( $"Unable to open log file '{path}'." );
            return false;
        }
        return true;
    }
}
