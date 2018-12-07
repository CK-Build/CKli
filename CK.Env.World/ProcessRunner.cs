using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CK.Env
{
    public static class ProcessRunner
    {
        /// <summary>
        /// Simple process run.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="workingDir">The working directory.</param>
        /// <param name="fileName">The file name to run.</param>
        /// <param name="arguments">Command arguments.</param>
        /// <param name="environmentVariables">Optional environment variables for the child process.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool Run(
                 IActivityMonitor m,
                 string workingDir,
                 string fileName,
                 string arguments,
                 IEnumerable<(string, string)> environmentVariables = null )
        {
            var encoding = Encoding.GetEncoding( 437 );
            ProcessStartInfo cmdStartInfo = new ProcessStartInfo
            {
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
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
                foreach( var kv in environmentVariables ) cmdStartInfo.EnvironmentVariables.Add( kv.Item1, kv.Item2 );
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
                    m.Error( $"Received errors on <StdErr>:" );
                    m.Error( errorCapture.ToString() );
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