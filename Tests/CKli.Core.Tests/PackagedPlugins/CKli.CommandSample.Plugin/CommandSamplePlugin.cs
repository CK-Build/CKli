using CK.Core;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Principal;
using System.Text;



namespace CKli.CommandSample.Plugin;

/// <summary>
/// The required default primary plugin.
/// </summary>
public sealed class CommandSamplePlugin : PluginBase
{
    readonly SupportWithCommand _tool;

    public CommandSamplePlugin( PrimaryPluginContext context, SupportWithCommand tool )
        : base( context )
    {
        Throw.CheckArgument( tool != null && tool.World == World );
        _tool = tool;
    }

    [FullCommandPath( "test echo" )]
    [Description( "Echoes the message, optionnaly in upper case." )]
    public bool Echo( IActivityMonitor monitor,
                      [Description("The message to write.")]
                      string message,
                      [Description("Writes the message in upper case.")]
                      bool upperCase )
    {
        var m = upperCase ? message.ToUpperInvariant() : message;
        monitor.Info( $"echo: {m}" );
        Console.WriteLine( m );
        return true;
    }

}
