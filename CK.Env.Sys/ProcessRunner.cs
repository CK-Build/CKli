using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using Microsoft.PowerShell;
using System;
using System.Threading;

namespace CK.Env
{
    /// <summary>
    /// Helpful encapsulation of <see cref="ProcessStartInfo"/> and <see cref="Process"/>.
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>
        /// Whether we are running on Unix.
        /// </summary>
        public static bool IsRunningOnUnix => Environment.OSVersion.Platform == PlatformID.Unix;

        /// <summary>
        /// Runs a .ps1 file by running "Powershell.exe" (or "pwsh" if <see cref="IsRunningOnUnix"/>) that
        /// must be in the path.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="workingDir">The working directory.</param>
        /// <param name="scriptFileName">The script file name.</param>
        /// <param name="arguments">The script arguments.</param>
        /// <param name="timeoutMilliseconds">Maximal time to wait for the process to terminate before killing it.</param>
        /// <param name="stdErrorLevel">Trace level of Standard Error stream.</param>
        /// <param name="environmentVariables">Optional environment variables for the child process.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool RunPowerShell(
                IActivityMonitor monitor,
                string workingDir,
                string scriptFileName,
                IEnumerable<string> arguments,
                int timeoutMilliseconds,
                LogLevel stdErrorLevel = LogLevel.Warn,
                IEnumerable<(string, string)>? environmentVariables = null )
        {
            if( !Path.IsPathRooted( scriptFileName ) && scriptFileName[0] != '.' )
            {
                scriptFileName = "./" + scriptFileName;
            }
            var fileName = '"' + scriptFileName + '"';
            foreach( var arg in arguments )
            {
                fileName += " " + arg;
            }

            return Run( monitor,
                        workingDir,
                        IsRunningOnUnix ? "pwsh" : "Powershell.exe",
                        "-executionpolicy unrestricted "+ fileName,
                        timeoutMilliseconds,
                        stdErrorLevel,
                        environmentVariables );
        }

        /// <summary>
        /// Simple process run.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="workingDir">The working directory.</param>
        /// <param name="fileName">The file name to run.</param>
        /// <param name="arguments">Command arguments.</param>
        /// <param name="timeoutMilliseconds">Maximal time to wait for the process to terminate before killing it.</param>
        /// <param name="stdErrorLevel">Trace level of Standard Error stream.</param>
        /// <param name="environmentVariables">Optional environment variables for the child process.</param>
        /// <returns>True on success (<see cref="Process.ExitCode"/> is equal to 0), false otherwise.</returns>
        public static bool Run( IActivityMonitor m,
                                string workingDir,
                                string fileName,
                                string arguments,
                                int timeoutMilliseconds,
                                LogLevel stdErrorLevel = LogLevel.Warn,
                                IEnumerable<(string, string)>? environmentVariables = null )
        {
            ProcessStartInfo cmdStartInfo = ConfigureProcessInfo( workingDir, fileName, arguments, environmentVariables );
            return Run( m, cmdStartInfo, timeoutMilliseconds, stdErrorLevel );
        }

        /// <summary>
        /// Configures a <see cref="ProcessStartInfo"/>.
        /// </summary>
        /// <param name="workingDir">The working directory.</param>
        /// <param name="fileName">The file name to run.</param>
        /// <param name="arguments">Command arguments.</param>
        /// <param name="environmentVariables">Optional environment variables for the child process.</param>
        /// <returns>A configured start info.</returns>
        public static ProcessStartInfo ConfigureProcessInfo( string workingDir, string fileName, string arguments, IEnumerable<(string, string)>? environmentVariables = null )
        {
            ProcessStartInfo cmdStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                // ErrorDialog = false, -- The default is false.
                // LoadUserProfile = false, -- The default is false.
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
            };
            if( environmentVariables != null )
            {
                foreach( (string key, string value) in environmentVariables )
                {
                    cmdStartInfo.EnvironmentVariables[key] = value;
                }
            }
            cmdStartInfo.Arguments = arguments;
            return cmdStartInfo;
        }

        /// <summary>
        /// Simple process run.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="startInfo">Process start info.</param>
        /// <param name="timeoutMilliseconds">Maximal time to wait for the process to terminate before killing it.</param>
        /// <param name="stdErrorLevel">Trace level of Standard Error stream.</param>
        /// <returns>True on success (<see cref="Process.ExitCode"/> is equal to 0), false otherwise.</returns>
        public static bool Run( IActivityMonitor m, ProcessStartInfo startInfo, int timeoutMilliseconds, LogLevel stdErrorLevel = LogLevel.Warn )
        {
            // Arguments defaults to empty. Corrects it here if ever it is null. 
            if( startInfo.Arguments == null ) startInfo.Arguments = string.Empty;
            else startInfo.Arguments = startInfo.Arguments.TrimStart();

            if( IsRunningOnUnix
                && startInfo.FileName.Equals( "cmd.exe", StringComparison.OrdinalIgnoreCase )
                && startInfo.Arguments.StartsWith( "/C " ) )
            {
                int idx = startInfo.Arguments.IndexOf( ' ', 3 );
                if( idx < 0 )
                {
                    startInfo.FileName = startInfo.Arguments.Substring( 3 );
                    startInfo.Arguments = String.Empty;
                }
                else
                {
                    startInfo.FileName = startInfo.Arguments.Substring( 3, idx - 3 );
                    startInfo.Arguments = startInfo.Arguments.Substring( idx );
                }
                m.Info( "Call to cmd.exe /C detected: since this cannot work on Unix platforms, this has been automatically adapted to directly call the command." );
                if( startInfo.FileName.Contains( '"' ) || startInfo.FileName.Contains( '\'' ) )
                {
                    m.Warn( "This adaptation is simple and naïve: the command name should not be quoted nor contain white escaped spaces. If this happens, please change the call to target the Unix command directly." );
                }
            }

            using( m.OpenTrace( $"{startInfo.FileName} {startInfo.Arguments}" ) )
            using( Process cmdProcess = new Process() )
            {
                StringBuilder errorCapture = new StringBuilder();
                DataReceivedEventHandler outputReceived = delegate ( object o, DataReceivedEventArgs e ) { if( e.Data != null ) m.Info( "<StdOut> " + e.Data ); };
                DataReceivedEventHandler errorReceived = delegate ( object o, DataReceivedEventArgs e ) { if( !string.IsNullOrEmpty( e.Data ) ) errorCapture.AppendLine( e.Data ); };

                cmdProcess.StartInfo = startInfo;
                cmdProcess.OutputDataReceived += outputReceived;
                cmdProcess.ErrorDataReceived += errorReceived;
                cmdProcess.Start();
                cmdProcess.BeginErrorReadLine();
                cmdProcess.BeginOutputReadLine();

                bool hasExited = cmdProcess.WaitForExit( timeoutMilliseconds );
                int exitCode = hasExited ? cmdProcess.ExitCode : 0;

                cmdProcess.OutputDataReceived -= outputReceived;
                cmdProcess.ErrorDataReceived -= errorReceived;
                // This flushes the streams and waits for the message pumps to end.
                cmdProcess.Close();

                if( !hasExited )
                {
                    Thread.Sleep( 50 );
                    using( m.OpenError( $"Process ran out of time ({timeoutMilliseconds} ms). Killing it (including its child processes)." ) )
                    {
                        try
                        {
                            cmdProcess.Kill( entireProcessTree: true );
                        }
                        catch( Exception ex )
                        {
                            m.Error( ex );
                        }
                    }
                    DumpStdErr( m, stdErrorLevel, errorCapture );
                    return false;
                }

                DumpStdErr( m, stdErrorLevel, errorCapture );
                if( exitCode != 0 )
                {
                    m.Error( $"Process returned ExitCode {exitCode}." );
                    return false;
                }
                return true;
            }

            static void DumpStdErr( IActivityMonitor m, LogLevel stdErrorLevel, StringBuilder errorCapture )
            {
                if( errorCapture.Length > 0 )
                {
                    using( m.OpenGroup( stdErrorLevel, "Received errors on <StdErr>:" ) )
                    {
                        m.Log( stdErrorLevel, errorCapture.ToString() );
                    }
                }
            }
        }
    }
}
