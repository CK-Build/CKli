using CK.Core;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CKli.Core;

/// <summary>
/// Minimal model that handles Ctrl+C and termination. InterruptibleScopes are stacked and only the
/// top most (the last created) receives a Ctrl+C (or <see cref="PosixSignal.SIGINT"/>)
/// that signals its <see cref="Alive"/> token but all of them receives a <see cref="PosixSignal.SIGTERM"/>.
/// <para>
/// Once a SIGTERM is received, no more InterruptibleScope can be created.
/// </para>
/// </summary>
public sealed class InterruptibleScope : IDisposable
{
    static readonly Lock _lock = new Lock();
    static readonly CancellationTokenSource _termination = new CancellationTokenSource();
    static InterruptibleScope? _top;

    readonly CancellationTokenSource _cancel;
    readonly InterruptibleScope? _prev;

    InterruptibleScope()
    {
        _cancel = new CancellationTokenSource();
        _prev = _top;
        _top = this;
    }

    internal static void Initialize()
    {
        // Register for SIGTERM (Commonly sent by Docker/Kubernetes/systemd).
        // This is handled to guaranty that all existing InterruptibleScope are signaled
        // and no more InterruptibleScope can be created.
        _ = PosixSignalRegistration.Create( PosixSignal.SIGTERM, HandleTerminationSignal );
        // Register for SIGINT (Ctrl+C on Windows/Linux) to signal and pop the topmost handler.
        _ = PosixSignalRegistration.Create( PosixSignal.SIGINT, HandleInterruptSignal );
    }

    static void HandleTerminationSignal( PosixSignalContext context )
    {
        // Prevents the process to be instantly killed.
        context.Cancel = true;
        ActivityMonitor.StaticLogger.Info( "Received SIGTERM signal." );
        Terminate();
    }

    static void HandleInterruptSignal( PosixSignalContext context )
    {
        context.Cancel = true;
        ActivityMonitor.StaticLogger.Info( "Received SIGINT signal." );
        CancellationTokenSource? toSignal = null;
        lock( _lock )
        {
            if( _top != null )
            {
                toSignal = _top._cancel;
                _top = _top._prev;
            }
        }
        toSignal?.Cancel();
    }

    /// <summary>
    /// Gets a token that is signaled when this scope is interrupted.
    /// </summary>
    public CancellationToken Alive => _cancel.Token;

    /// <summary>
    /// Closes this scope. <see cref="Alive"/> is left as-is.
    /// </summary>
    public void Dispose()
    {
        lock( _lock )
        {
            if( _top == this )
            {
                _top = _prev;
            }
        }
    }

    /// <summary>
    /// Creates a new interruptible scope. Null if a <see cref="PosixSignal.SIGTERM"/> has been received
    /// or <see cref="Terminate"/> has been called.
    /// </summary>
    /// <returns></returns>
    public static InterruptibleScope? Create()
    {
        lock( _lock )
        {
            return _termination.IsCancellationRequested ? null : new InterruptibleScope();
        }
    }

    /// <summary>
    /// Terminates this process: <see cref="Termination"/> is signaled, all existing <see cref="InterruptibleScope"/>
    /// are signaled, no more can be created.
    /// </summary>
    public static void Terminate()
    {
        _termination.Cancel();
        InterruptibleScope? top;
        lock( _lock )
        {
            top = _top;
            _top = null;
        }
        while( top != null )
        {
            top._cancel.Cancel();
            top = top._prev;
        }
    }

    /// <summary>
    /// Gets a token that is signaled when a <see cref="PosixSignal.SIGTERM"/> is received (or <see cref="Terminate"/> has been called).
    /// The current process should end (this must end any interactive mode and leave the process).
    /// </summary>
    public static CancellationToken Termination => _termination.Token;

}
