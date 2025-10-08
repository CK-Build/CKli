using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CKli.Core;

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
    }

    public void OnGroupClosing( IActivityLogGroup group, ref List<ActivityLogGroupConclusion>? conclusions )
    {
    }

    public void OnOpenGroup( IActivityLogGroup group )
    {
        OnUnfilteredLog( ref group.Data );
    }

    public void OnTopicChanged( string newTopic, string? fileName, int lineNumber )
    {
    }

    public void OnUnfilteredLog( ref ActivityMonitorLogData data )
    {
        if( data.MaskedLevel >= LogLevel.Warn )
        {
            if( data.MaskedLevel == LogLevel.Warn ) _screen.DisplayWarning( data.Text );
            else _screen.DisplayError( data.Text );
        }
        else
        {
            _screen.OnLogText( data.Text );
        }
    }
}
