using CK.Core;
using System;
using System.Diagnostics;
using System.IO;
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
                return OpenPathLinux( monitor, path, isFolder );
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
        // Use cmd.exe with 'start' command to open the file/folder in a detached process.
        // This prevents the opened application from writing to our console.
        // For instance, VSCode pollutes the console with lines like:
        // "[main 2025-11-04T08:27:57.303Z] update#setState idle"
        using var p = Process.Start( new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"\" \"{path}\"",
            UseShellExecute = false,
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

    static bool OpenPathLinux( IActivityMonitor monitor, string path, bool isFolder )
    {
        if( isFolder )
        {
            if( RunShell( "xdg-open \"$1\" >/dev/null 2>&1", path ) != 0 )
            {
                Console.Out.WriteLine( path );
            }
            return true;
        }
        var scripts = LinuxLogViewer.GetScripts( Environment.GetEnvironmentVariable( "CKLI_PAGER" ),
                                                 Environment.GetEnvironmentVariable( "PAGER" ),
                                                 Console.IsOutputRedirected );
        foreach( var script in scripts )
        {
            int code = RunShell( script, path );
            if( code == 0 ) return true;
            // 127 is the shell's "command not found": the viewer is simply not installed.
            if( code == 127 ) monitor.Trace( $"Log viewer '{script}' is not available." );
            else monitor.Warn( $"Log viewer '{script}' failed with exit code {code}." );
        }
        using var file = File.OpenRead( path );
        using var stdout = Console.OpenStandardOutput();
        file.CopyTo( stdout );
        return true;
    }

    // Runs a /bin/sh script that receives the path as "$1". The child process owns the terminal.
    static int RunShell( string script, string path )
    {
        using var p = Process.Start( new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { "-c", script, "sh", path },
            UseShellExecute = false
        } );
        if( p == null ) return -1;
        p.WaitForExit();
        return p.ExitCode;
    }
}
