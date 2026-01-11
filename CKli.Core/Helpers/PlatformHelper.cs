using CK.Core;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CKli.Core;

/// <summary>
/// Platform-specific utilities for cross-platform operations.
/// </summary>
static class PlatformHelper
{
    /// <summary>
    /// Opens a file with the system's default application.
    /// </summary>
    /// <param name="monitor">Activity monitor for logging.</param>
    /// <param name="path">Path to the file to open.</param>
    /// <returns>True if the file was opened successfully, false otherwise.</returns>
    public static bool OpenFile( IActivityMonitor monitor, string path )
    {
        return OpenPath( monitor, path, isFolder: false );
    }

    /// <summary>
    /// Opens a folder in the system's file explorer.
    /// </summary>
    /// <param name="monitor">Activity monitor for logging.</param>
    /// <param name="path">Path to the folder to open.</param>
    /// <returns>True if the folder was opened successfully, false otherwise.</returns>
    public static bool OpenFolder( IActivityMonitor monitor, string path )
    {
        return OpenPath( monitor, path, isFolder: true );
    }

    static bool OpenPath( IActivityMonitor monitor, string path, bool isFolder )
    {
        try
        {
            if( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
            {
                return OpenPathWindows( monitor, path, isFolder );
            }
            else if( RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) )
            {
                return OpenPathMacOS( monitor, path );
            }
            else if( RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) )
            {
                return OpenPathLinux( monitor, path );
            }
            else
            {
                monitor.Error( $"Opening files is not supported on this platform. Path: {path}" );
                return false;
            }
        }
        catch( Exception ex )
        {
            monitor.Error( $"Unable to open '{path}'.", ex );
            return false;
        }
    }

    static bool OpenPathWindows( IActivityMonitor monitor, string path, bool isFolder )
    {
        using var p = Process.Start( new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            Verb = isFolder ? "open" : string.Empty,
            CreateNoWindow = true
        } );
        return p != null;
    }

    static bool OpenPathMacOS( IActivityMonitor monitor, string path )
    {
        using var p = Process.Start( new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        } );
        return p != null;
    }

    static bool OpenPathLinux( IActivityMonitor monitor, string path )
    {
        if( IsCommandAvailable( "xdg-open" ) )
        {
            using var p = Process.Start( new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            } );
            return p != null;
        }
        else
        {
            monitor.Error( $"Unable to open '{path}'. Install xdg-utils or open manually." );
            return false;
        }
    }

    /// <summary>
    /// Checks if a command is available on the system (Unix platforms only).
    /// </summary>
    /// <param name="command">The command name to check.</param>
    /// <returns>True if the command exists, false otherwise.</returns>
    static bool IsCommandAvailable( string command )
    {
        try
        {
            using var p = Process.Start( new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            } );
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
