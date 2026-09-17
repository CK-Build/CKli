using CK.Core;
using CK.Monitoring;
using CK.Testing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Collects the text of every log entry that reaches the <see cref="GrandOutput"/>, whatever emitted it.
/// Obtained by <see cref="CKliTestHelperExtensions.CollectAllTexts(IMonitorTestHelper)"/> and must be disposed.
/// <para>
/// This is what <c>TestHelper.Monitor.CollectTexts(...)</c> cannot do: that one is an
/// <see cref="IActivityMonitorClient"/> on a single monitor, so it never sees an entry emitted by another one -
/// a roadmap builds up to <c>--max-dop</c> solutions, each on its own monitor, and
/// <see cref="ActivityMonitor.StaticLogger"/> belongs to no monitor at all. A <see cref="IGrandOutputHandler"/>
/// sits after the dispatcher and therefore sees them all.
/// </para>
/// </summary>
public sealed class AllMonitorsTextCollector : IGrandOutputHandler, IDisposable
{
    readonly GrandOutput _output;
    readonly List<string> _texts;
    int _disposed;

    internal AllMonitorsTextCollector( GrandOutput output )
    {
        _output = output;
        _texts = new List<string>();
        _output.Sink.SubmitAddHandler( this );
    }

    /// <summary>
    /// Gets the collected texts, in the order the dispatcher handled them.
    /// <para>
    /// Reading this first waits for the entries still queued to be dispatched (<see cref="DispatcherSink.SyncWait"/>):
    /// the GrandOutput is asynchronous, so without it an assertion can run before the entry it is about has
    /// been handled. This is why the texts are read through a property instead of an "out" list.
    /// </para>
    /// </summary>
    public ImmutableArray<string> Texts
    {
        get
        {
            _output.Sink.SyncWait();
            lock( _texts )
            {
                return _texts.ToImmutableArray();
            }
        }
    }

    ValueTask<bool> IGrandOutputHandler.ActivateAsync( IActivityMonitor monitor ) => ValueTask.FromResult( true );

    ValueTask<bool> IGrandOutputHandler.ApplyConfigurationAsync( IActivityMonitor monitor, IHandlerConfiguration c ) => ValueTask.FromResult( false );

    ValueTask IGrandOutputHandler.DeactivateAsync( IActivityMonitor monitor ) => default;

    ValueTask IGrandOutputHandler.OnTimerAsync( IActivityMonitor monitor, TimeSpan timerSpan ) => default;

    ValueTask IGrandOutputHandler.HandleAsync( IActivityMonitor monitor, InputLogEntry logEvent )
    {
        var t = logEvent.Text;
        if( t != null )
        {
            lock( _texts )
            {
                _texts.Add( t );
            }
        }
        return default;
    }

    /// <summary>
    /// Removes this collector from the <see cref="GrandOutput"/>.
    /// </summary>
    public void Dispose()
    {
        if( System.Threading.Interlocked.Increment( ref _disposed ) == 1 )
        {
            _output.Sink.SubmitRemoveHandler( this );
        }
    }
}
