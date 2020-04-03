using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using Microsoft.PowerShell;

namespace CK.Env
{
    /// <summary>
    /// Helpful encapsulation of <see cref="ProcessStartInfo"/> and <see cref="Process"/>.
    /// </summary>
    public static class ProcessRunner
    {
        /// <summary>
        /// Runs a .ps1 file by running "Powershell.exe" that must be in the path.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="workingDir">The working directory.</param>
        /// <param name="scriptFileName">The script file name.</param>
        /// <param name="arguments">The script arguments.</param>
        /// <param name="stdErrorLevel">Trace level of Standard Error stream.</param>
        /// <param name="environmentVariables">Optional environment variables for the child process.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool RunPowerShell(
                IActivityMonitor m,
                string workingDir,
                string scriptFileName,
                IEnumerable<string> arguments,
                LogLevel stdErrorLevel = LogLevel.Warn,
                IEnumerable<(string, string)> environmentVariables = null )
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

            var state = InitialSessionState.Create();
            state.ExecutionPolicy = ExecutionPolicy.Bypass;
            using( var ps = PowerShell.Create( state ) )
            {
                ps.Invoke( fileName );
            }
            return Run( m, workingDir, "Powershell.exe", fileName, stdErrorLevel, environmentVariables );
        }

        /// <summary>
        /// Simple process run.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="workingDir">The working directory.</param>
        /// <param name="fileName">The file name to run.</param>
        /// <param name="arguments">Command arguments.</param>
        /// <param name="stdErrorLevel">Trace level of Standard Error stream.</param>
        /// <param name="environmentVariables">Optional environment variables for the child process.</param>
        /// <returns>True on success (<see cref="Process.ExitCode"/> is equal to 0), false otherwise.</returns>
        public static bool Run(
                 IActivityMonitor m,
                 string workingDir,
                 string fileName,
                 string arguments,
                 LogLevel stdErrorLevel = LogLevel.Warn,
                 IEnumerable<(string, string)> environmentVariables = null )
        {
            ProcessStartInfo cmdStartInfo = ConfigureProcessInfo( workingDir, fileName, arguments, environmentVariables );
            return Run( m, cmdStartInfo, stdErrorLevel );
        }

        /// <summary>
        /// Configures a <see cref="ProcessStartInfo"/>.
        /// </summary>
        /// <param name="workingDir">The working directory.</param>
        /// <param name="fileName">The file name to run.</param>
        /// <param name="arguments">Command arguments.</param>
        /// <param name="environmentVariables">Optional environment variables for the child process.</param>
        /// <returns>A configured start info.</returns>
        public static ProcessStartInfo ConfigureProcessInfo( string workingDir, string fileName, string arguments, IEnumerable<(string, string)> environmentVariables = null )
        {
            ProcessStartInfo cmdStartInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
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
        /// <param name="stdErrorLevel">Trace level of Standard Error stream.</param>
        /// <returns>True on success (<see cref="Process.ExitCode"/> is equal to 0), false otherwise.</returns>
        public static bool Run( IActivityMonitor m, ProcessStartInfo startInfo, LogLevel stdErrorLevel = LogLevel.Warn )
        {
            using( m.OpenTrace( $"{startInfo.FileName} {startInfo.Arguments}" ) )
            using( Process cmdProcess = new Process() )
            {
                StringBuilder errorCapture = new StringBuilder();
                cmdProcess.StartInfo = startInfo;
                cmdProcess.ErrorDataReceived += ( o, e ) => { if( !string.IsNullOrEmpty( e.Data ) ) errorCapture.AppendLine( e.Data ); };
                cmdProcess.OutputDataReceived += ( o, e ) => { if( e.Data != null ) m.Info( "<StdOut> " + e.Data ); };
                cmdProcess.Start();
                cmdProcess.BeginErrorReadLine();
                cmdProcess.BeginOutputReadLine();
                cmdProcess.WaitForExit();
                if( errorCapture.Length > 0 )
                {
                    using( m.OpenGroup( stdErrorLevel, "Received errors on <StdErr>:" ) )
                    {
                        m.Log( stdErrorLevel, errorCapture.ToString() );
                    }
                }
                if( cmdProcess.ExitCode != 0 )
                {
                    m.Error( $"Process returned ExitCode {cmdProcess.ExitCode}." );
                    return false;
                }
                return true;
            }
        }
    }
}
