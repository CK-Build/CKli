using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Activity monitor client that binds a <see cref="IActivityMonitor"/> to a <see cref="IScreen"/>.
/// </summary>
public sealed class ScreenLogger : IActivityMonitorClient
{
    readonly IScreen _screen;

    /// <summary>
    /// Initializes a new <see cref="ScreenLogger"/> on a screen.
    /// </summary>
    /// <param name="screen">The target screen.</param>
    public ScreenLogger( IScreen screen )
    {
        _screen = screen;
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
}
