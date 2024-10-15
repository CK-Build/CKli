using CK.Core;
using System;

namespace CKli;

public static class CommandContext
{
    public static int WithMonitor( Func<IActivityMonitor,int> run )
    {
        ActivityMonitor.DefaultFilter = LogFilter.Diagnostic;
        var monitor = new ActivityMonitor( ActivityMonitorOptions.WithInitialReplay );
        try
        {
            return run( monitor );
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return -1;
        }
        finally
        {
            var output = new ColoredActivityMonitorConsoleClient();
            monitor.Output.RegisterClient( output, replayInitialLogs: true );
        }
    }
}
