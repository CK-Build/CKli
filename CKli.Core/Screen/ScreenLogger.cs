using CK.Core;
using CK.Monitoring;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Activity monitor client that binds a <see cref="IActivityMonitor"/> to a <see cref="IScreen"/>.
/// </summary>
public sealed class ScreenLogger : IActivityMonitorClient
{
    readonly IScreen _screen;

    /// <summary>
    /// Initializes a new <see cref="ScreenLogger"/> on a screen.
    /// When a <paramref name="grandOutput"/> is provided, logs emitted by <see cref="IStaticLogger"/>
    /// or <see cref="IParallelLogger"/> are tracked and provided to the screen.
    /// </summary>
    /// <param name="monitor">The monitor to which this client is bound.</param>
    /// <param name="screen">The target screen.</param>
    /// <param name="grandOutput">The GrandOutput instance from which background logs must be tracked.</param>
    public ScreenLogger( IActivityMonitor monitor, IScreen screen, GrandOutput? grandOutput )
    {
        _screen = screen;
        if( grandOutput != null )
        {
            grandOutput.Sink.SubmitAddHandler( new ParallelLogTracker( monitor.UniqueId, screen ) );
        }
    }

    void IActivityMonitorClient.OnAutoTagsChanged( CKTrait newTrait )
    {
    }

    void IActivityMonitorClient.OnGroupClosed( IActivityLogGroup group, IReadOnlyList<ActivityLogGroupConclusion> conclusions )
    {
        _screen.OnLog( LogLevel.None, null, false );
    }

    void IActivityMonitorClient.OnGroupClosing( IActivityLogGroup group, ref List<ActivityLogGroupConclusion>? conclusions )
    {
    }

    void IActivityMonitorClient.OnOpenGroup( IActivityLogGroup group ) => OnLog( ref group.Data, true );

    void IActivityMonitorClient.OnTopicChanged( string newTopic, string? fileName, int lineNumber )
    {
    }

    void IActivityMonitorClient.OnUnfilteredLog( ref ActivityMonitorLogData data ) => OnLog( ref data, false );

    void OnLog( ref ActivityMonitorLogData data, bool isOpenGroup )
    {
        var l = data.MaskedLevel;
        if( l >= LogLevel.Warn || data.Tags.Overlaps( ScreenType.CKliScreenTag ) )
        {
            _screen.ScreenLog( l, data.Text );
        }
        _screen.OnLog( l, data.Text, isOpenGroup );
    }

    sealed class ParallelLogTracker : IGrandOutputHandler
    {
        readonly string _monitorId;
        readonly IScreen _screen;
        static readonly CKTrait _processRunnerTag = ProcessRunner.StdOutTag | ProcessRunner.StdErrTag;

        public ParallelLogTracker( string monitorId, IScreen screen )
        {
            _monitorId = monitorId;
            _screen = screen;
        }

        public ValueTask<bool> ActivateAsync( IActivityMonitor monitor ) => ValueTask.FromResult( true );

        public ValueTask<bool> ApplyConfigurationAsync( IActivityMonitor monitor, IHandlerConfiguration c ) => ValueTask.FromResult( false );

        public ValueTask DeactivateAsync( IActivityMonitor monitor ) => default;

        public ValueTask OnTimerAsync( IActivityMonitor monitor, TimeSpan timerSpan ) => default;

        public ValueTask HandleAsync( IActivityMonitor monitor, InputLogEntry logEvent )
        {
            Throw.DebugAssert( (logEvent.MonitorId == _monitorId) == ReferenceEquals( logEvent.MonitorId, _monitorId ) );
            if( !ReferenceEquals( logEvent.MonitorId, _monitorId ) || !(logEvent.Tags & _processRunnerTag).IsEmpty )
            {
                var t = logEvent.Text;
                if( t != null ) _screen.OnParallelText( t );
            }
            return default;
        }

    }
}



