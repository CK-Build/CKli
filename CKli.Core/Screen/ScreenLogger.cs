using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Activity monitor client that binds a <see cref="IActivityMonitor"/> to a <see cref="IScreen"/>.
/// </summary>
public sealed class ScreenLogger : IActivityMonitorClient
{
    readonly IScreen _screen;

    public ScreenLogger( IScreen screen )
    {
        _screen = screen;
    }

    public void OnAutoTagsChanged( CKTrait newTrait )
    {
    }

    public void OnGroupClosed( IActivityLogGroup group, IReadOnlyList<ActivityLogGroupConclusion> conclusions )
    {
        _screen.OnLogAny( LogLevel.None, null, false );
    }

    public void OnGroupClosing( IActivityLogGroup group, ref List<ActivityLogGroupConclusion>? conclusions )
    {
    }

    public void OnOpenGroup( IActivityLogGroup group ) => OnLog( ref group.Data, true );

    public void OnTopicChanged( string newTopic, string? fileName, int lineNumber )
    {
    }

    public void OnUnfilteredLog( ref ActivityMonitorLogData data ) => OnLog( ref data, false );

    void OnLog( ref ActivityMonitorLogData data, bool isOpenGroup )
    {
        var l = data.MaskedLevel;
        if( l >= LogLevel.Warn )
        {
            _screen.OnLogErrorOrWarning( l, data.Text );
        }
        else
        {
            _screen.OnLogAny( l, data.Text, isOpenGroup );
        }
    }
}
