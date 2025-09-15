using CK.Core;
using CKli.Core;
using System;

namespace CKli;

static class CommandContext
{
    public static LogFilter LogFilter;

    public static IUserPreferences UserPreferences = new DefaultUserPreferences();

    public static int Run( Func<IActivityMonitor,int> run )
    {
        ActivityMonitor.DefaultFilter = LogFilter;
        var monitor = new ActivityMonitor();
        var output = new ColoredActivityMonitorConsoleClient();
        monitor.Output.RegisterClient( output );
        try
        {
            return run( monitor );
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return -1;
        }
    }

    public static int Run( Func<IActivityMonitor, IUserPreferences, int> run ) => Run( monitor => run( monitor, UserPreferences ) );
}
