using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace CKli.Core;

/// <summary>
/// From MSBuild.
/// </summary>
static class AnsiDetector
{
    [SupportedOSPlatformGuard( "windows" )]
    static readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    const int STD_OUTPUT_HANDLE = -11;
    const int STD_ERROR_HANDLE = -12;
    const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    const uint FILE_TYPE_CHAR = 0x0002;

    internal enum StreamHandleType
    {
        StdOut = STD_OUTPUT_HANDLE,
        StdErr = STD_ERROR_HANDLE,
    };

    internal static (bool acceptAnsiColorCodes, uint? originalConsoleMode) TryEnableAnsiColorCodes( StreamHandleType handleType = StreamHandleType.StdOut )
    {
        if( Console.IsOutputRedirected )
        {
            // There's no ANSI terminal support if console output is redirected.
            return (acceptAnsiColorCodes: false, originalConsoleMode: null);
        }

        if( Console.BufferHeight == 0 || Console.BufferWidth == 0 )
        {
            // The current console doesn't have a valid buffer size, which means it is not a real console. let's default to not using TL
            // in those scenarios.
            return (acceptAnsiColorCodes: false, originalConsoleMode: null);
        }

        bool acceptAnsiColorCodes = false;
        uint? originalConsoleMode = null;
        if( _isWindows )
        {
            try
            {
                IntPtr outputStream = GetStdHandle( (int)handleType );
                if( GetConsoleMode( outputStream, out uint consoleMode ) )
                {
                    if( (consoleMode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) == ENABLE_VIRTUAL_TERMINAL_PROCESSING )
                    {
                        // Console is already in required state.
                        acceptAnsiColorCodes = true;
                    }
                    else
                    {
                        originalConsoleMode = consoleMode;
                        consoleMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                        if( SetConsoleMode( outputStream, consoleMode ) && GetConsoleMode( outputStream, out consoleMode ) )
                        {
                            // We only know if vt100 is supported if the previous call actually set the new flag, older
                            // systems ignore the setting.
                            acceptAnsiColorCodes = (consoleMode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) == ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                        }
                    }

                    uint fileType = GetFileType( outputStream );
                    // The std out is a char type (LPT or Console).
                    acceptAnsiColorCodes &= fileType == FILE_TYPE_CHAR;
                }
            }
            catch
            {
                // In the unlikely case that the above fails we just ignore and continue.
            }
        }
        else
        {
            // On posix OSes detect whether the terminal supports VT100 from the value of the TERM environment variable.
            acceptAnsiColorCodes = PosixDetector.IsAnsiSupported( Environment.GetEnvironmentVariable( "TERM" ) );
        }
        return (acceptAnsiColorCodes, originalConsoleMode);
    }

    internal static void RestoreConsoleMode( uint? originalConsoleMode  )
    {
        if( _isWindows && originalConsoleMode is not null )
        {
            IntPtr stdOut = GetStdHandle( (int)StreamHandleType.StdOut );
            _ = SetConsoleMode( stdOut, originalConsoleMode.Value );
        }
    }

    [DllImport( "kernel32.dll" )]
    [SupportedOSPlatform( "windows" )]
    internal static extern IntPtr GetStdHandle( int nStdHandle );

    [DllImport( "kernel32.dll" )]
    [SupportedOSPlatform( "windows" )]
    internal static extern uint GetFileType( IntPtr hFile );

    [DllImport( "kernel32.dll" )]
    internal static extern bool GetConsoleMode( IntPtr hConsoleHandle, out uint lpMode );

    [DllImport( "kernel32.dll" )]
    internal static extern bool SetConsoleMode( IntPtr hConsoleHandle, uint dwMode );


    internal class PosixDetector
    {
        private static readonly Regex[] terminalsRegexes =
        {
            new("^xterm"), // xterm, PuTTY, Mintty
            new("^rxvt"), // RXVT
            new("^(?!eterm-color).*eterm.*"), // Accepts eterm, but not eterm-color, which does not support moving the cursor, see #9950.
            new("^screen"), // GNU screen, tmux
            new("tmux"), // tmux
            new("^vt100"), // DEC VT series
            new("^vt102"), // DEC VT series
            new("^vt220"), // DEC VT series
            new("^vt320"), // DEC VT series
            new("ansi"), // ANSI
            new("scoansi"), // SCO ANSI
            new("cygwin"), // Cygwin, MinGW
            new("linux"), // Linux console
            new("konsole"), // Konsole
            new("bvterm"), // Bitvise SSH Client
            new("^st-256color"), // Suckless Simple Terminal, st
            new("alacritty"), // Alacritty
        };

        internal static bool IsAnsiSupported( string? termType )
        {
            if( string.IsNullOrEmpty( termType ) )
            {
                return false;
            }

            if( terminalsRegexes.Any( regex => regex.IsMatch( termType ) ) )
            {
                return true;
            }

            return false;
        }
    }
}
