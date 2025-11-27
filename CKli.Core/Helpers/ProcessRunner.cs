using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CKli.Core;

/// <summary>
/// Basic external process runner encapsulation.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Tag for <see cref="Process.StandardError"/> log lines.
    /// </summary>
    public static readonly CKTrait StdErrTag = ActivityMonitor.Tags.Register( "StdErr" );

    /// <summary>
    /// Tag for <see cref="Process.StandardOutput"/> log lines.
    /// </summary>
    public static readonly CKTrait StdOutTag = ActivityMonitor.Tags.Register( "StdOut" );

    /// <summary>
    /// Starts and wait for the end of an external process, optionally handles a tiemout and
    /// standard output and/or error capture. Always log the output and error as <see cref="LogLevel.Trace"/>
    /// and <see cref="StdOutTag"/> or <see cref="StdErrTag"/>.
    /// <para>
    /// This is a very basic helper that suits our needs.
    /// For more complex needs, you'd better use Cli.Wrap (https://github.com/Tyrrrz/CliWrap).
    /// </para>
    /// </summary>
    /// <param name="logger">The logger to use. Will receive standard errors and outputs.</param>
    /// <param name="fileName">The file name to run.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="workingDirectory">Working directory.</param>
    /// <param name="environmentVariables">Optional environment variables to configure.</param>
    /// <param name="timeout">Optional timeout in milliseconds.</param>
    /// <param name="stdOut">Optional standard output collector.</param>
    /// <param name="stdErr">Optional standard error collector.</param>
    /// <returns>The exit status code or null if timeout occured.</returns>
    public static int? RunProcess( IActivityLineEmitter logger,
                                   string fileName,
                                   string arguments,
                                   string workingDirectory,
                                   Dictionary<string, string>? environmentVariables = null,
                                   int timeout = Timeout.Infinite,
                                   StringBuilder? stdOut = null,
                                   StringBuilder? stdErr = null )
    {
        var info = new ProcessStartInfo( fileName, arguments )
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if( environmentVariables != null && environmentVariables.Count > 0 )
        {
            foreach( var kv in environmentVariables ) info.EnvironmentVariables.Add( kv.Key, kv.Value );
        }
        using var process = new Process { StartInfo = info };
        if( stdOut == null )
        {
            process.OutputDataReceived += ( sender, data ) =>
            {
                if( data.Data != null ) logger.Trace( StdOutTag, data.Data );
            };
        }
        else
        {
            process.OutputDataReceived += ( sender, data ) =>
            {
                if( data.Data != null )
                {
                    logger.Trace( StdOutTag, data.Data );
                    stdOut.AppendLine( data.Data );
                }
            };
        }
        if( stdErr == null )
        {
            process.ErrorDataReceived += ( sender, data ) =>
            {
                if( data.Data != null ) logger.Trace( StdErrTag, data.Data );
            };
        }
        else
        {
            process.ErrorDataReceived += ( sender, data ) =>
            {
                if( data.Data != null )
                {
                    logger.Trace( StdErrTag, data.Data );
                    stdErr.AppendLine( data.Data );
                }
            };
        }
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if( timeout > 0 )
        {
            bool exited = process.WaitForExit( timeout );
            if( exited )
            {
                // Ensure completed asynchronous event handling.
                // See https://github.com/NuGet/Home/issues/10189
                process.WaitForExit();
            }
            return exited ? process.ExitCode : null;
        }
        process.WaitForExit();
        return process.ExitCode;
    }

}
