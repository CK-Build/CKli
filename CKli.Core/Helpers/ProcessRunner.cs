using CK.Core;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
    /// How long <see cref="RunProcess"/> waits for the redirected streams to be closed once the process
    /// has exited: 30 seconds. This never expires unless another process is holding them open - see the
    /// remarks of <see cref="RunProcess"/>.
    /// </summary>
    public const int DrainTimeout = 30_000;

    /// <summary>
    /// Starts and wait for the end of an external process, optionally handles a timeout and
    /// standard output and/or error capture.
    /// <para>
    /// The <c>CKliCalled</c> environment variable is set to <c>true</c> and the <c>DOTNET_CLI_UI_LANGUAGE</c> is
    /// set to <c>en-us</c>.
    /// </para>
    /// <para>
    /// This doesn't open a log group, logs the <paramref name="arguments"/> nor the exit code and this is intended:
    /// arguments may contain sensitive information and running a process may be "hidden". By setting <paramref name="noLog"/>
    /// to true, nothing is logged otherwise, by default, the standard output and error are <see cref="LogLevel.Trace"/>
    /// with <see cref="StdOutTag"/> and <see cref="StdErrTag"/>.
    /// </para>
    /// <para>
    /// This is a very basic helper that suits our needs.
    /// For more complex needs, you'd better use Cli.Wrap (https://github.com/Tyrrrz/CliWrap).
    /// </para>
    /// </summary>
    /// <remarks>
    /// Waiting for a process whose output is redirected is TWO waits, and only the first one is about the
    /// process. A redirected stream reaches its end when every handle on its write end is closed, and a
    /// process that the child started has inherited those handles: it keeps the stream open for as long as
    /// it lives, whatever the child did. MSBuild's worker nodes do exactly that - with node reuse they
    /// outlive their build on purpose - so waiting for the end of the stream without a bound is a deadlock
    /// rather than a delay, and it is one this stack has actually hit.
    /// <para>
    /// Hence <see cref="Timeout.Infinite"/> is never handed to <see cref="Process.WaitForExit(int)"/>: that
    /// single value is what makes it wait for the streams too, unbounded. The process is waited for on its
    /// own, then the streams are given <see cref="DrainTimeout"/> and a warning if they do not close - the
    /// exit code is known by then, and the handlers have already received everything the child wrote.
    /// </para>
    /// </remarks>
    /// <param name="logger">The logger to use. Will receive standard errors and outputs.</param>
    /// <param name="fileName">The file name to run.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="workingDirectory">Working directory.</param>
    /// <param name="environmentVariables">Optional environment variables to configure.</param>
    /// <param name="timeout">Optional timeout in milliseconds.</param>
    /// <param name="stdOut">Optional standard output collector.</param>
    /// <param name="stdErr">Optional standard error collector.</param>
    /// <param name="noLog">True to log the standard output and error.</param>
    /// <param name="cancellation">
    /// Optional cancellation.
    /// Currently not used as we wait for .Net 11 (see https://devblogs.microsoft.com/dotnet/process-api-improvements-in-dotnet-11/)
    /// to use SINGINT/SINGTERM instead of brutal kill which is too dangerous.
    /// </param>
    /// <returns>The exit status code or null if timeout or cancellation occurred.</returns>
    public static int? RunProcess( IActivityLineEmitter logger,
                                   string fileName,
                                   string arguments,
                                   string workingDirectory,
                                   Dictionary<string, string?>? environmentVariables = null,
                                   int timeout = Timeout.Infinite,
                                   StringBuilder? stdOut = null,
                                   StringBuilder? stdErr = null,
                                   bool noLog = false,
                                   CancellationToken cancellation = default )
    {
        var blind = stdOut == null && stdErr == null && noLog;
        var info = new ProcessStartInfo( fileName, arguments )
        {
            WorkingDirectory = workingDirectory,
        };
        if( !blind )
        {
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
        }
        // Don't use EnvironmentVariables.Add!
        // When the environment variable already exists for this process, Add triggers
        // a marvelous "Value does not fall within the expected range." exception.
        info.EnvironmentVariables["CKliCalled"] = "true";
        info.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en-us";        
        // Removes the cause of the deadlock described above rather than merely bounding it: an MSBuild
        // worker node that is not reused exits with its build and lets go of the inherited handles. Reuse
        // buys nothing here anyway - CKli builds a different solution every time - and this is what CI
        // guidance recommends. It is set for every process: anything else ignores it.
        info.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";
        if( environmentVariables != null && environmentVariables.Count > 0 )
        {
            foreach( var kv in environmentVariables ) info.EnvironmentVariables[ kv.Key ] = kv.Value;
        }
        if( cancellation.IsCancellationRequested ) return null;

        using var process = new Process { StartInfo = info };
        // A null Data is the end-of-stream sentinel: it is the only signal that the redirected stream has
        // been closed, and it is what the bounded wait below waits for. These are deliberately not
        // disposable: a handler can still fire long after this method returned, when whoever inherited the
        // pipe finally exits, and completing an already completed source is a no-op.
        var outClosed = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var errClosed = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        if( blind )
        {
            outClosed.SetResult();
            errClosed.SetResult();
        }
        else
        {
            // Both handlers are always registered when the streams are redirected: BeginOutputReadLine and
            // BeginErrorReadLine read them whether anybody listens or not, and without a handler the
            // sentinel would be lost.
            process.OutputDataReceived += ( sender, data ) =>
            {
                if( data.Data == null )
                {
                    outClosed.TrySetResult();
                    return;
                }
                if( !noLog ) logger.Trace( StdOutTag, data.Data );
                stdOut?.AppendLine( data.Data );
            };
            process.ErrorDataReceived += ( sender, data ) =>
            {
                if( data.Data == null )
                {
                    errClosed.TrySetResult();
                    return;
                }
                if( !noLog ) logger.Trace( StdErrTag, data.Data );
                stdErr?.AppendLine( data.Data );
            };
        }
        process.Start();
        if( !blind )
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        //// Waiting for .Net 11.
        //using var stop = cancellation.CanBeCanceled
        //                    ? cancellation.UnsafeRegister( p => ((Process)p!).Kill(), process )
        //                    : default;

        // Timeout.Infinite is NOT passed here: it is the one value for which WaitForExit also waits for
        // the redirected streams to be closed, and that wait has no bound. int.MaxValue is the same 24 days
        // of patience without the hidden second wait. See this method's remarks.
        if( !process.WaitForExit( timeout > 0 ? timeout : int.MaxValue ) )
        {
            return null;
        }
        // Ensure completed asynchronous event handling, which the overload above skips.
        // See https://github.com/NuGet/Home/issues/10189
        if( !outClosed.Task.Wait( DrainTimeout ) || !errClosed.Task.Wait( DrainTimeout ) )
        {
            logger.Warn( $"Process '{fileName}' has exited but its redirected output is still open after "
                         + $"{DrainTimeout} ms: a process it started has inherited the pipe and outlives it. "
                         + "The captured output may be incomplete." );
        }
        return process.ExitCode;
    }

}
