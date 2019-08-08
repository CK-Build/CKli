using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

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
            var a = '"' + scriptFileName + '"';
            foreach( var arg in arguments )
            {
                a += " " + arg;
            }
            return Run( m, workingDir, "Powershell.exe", a, stdErrorLevel, environmentVariables );
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
        /// <returns>True on success, false on error.</returns>
        public static bool Run(
                 IActivityMonitor m,
                 string workingDir,
                 string fileName,
                 string arguments,
                 LogLevel stdErrorLevel = LogLevel.Warn,
                 IEnumerable<(string, string)> environmentVariables = null )
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
            using( m.OpenTrace( $"{fileName} {cmdStartInfo.Arguments}" ) )
            using( Process cmdProcess = new Process() )
            {
                StringBuilder errorCapture = new StringBuilder();
                cmdProcess.StartInfo = cmdStartInfo;
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
