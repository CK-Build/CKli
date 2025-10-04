using CK.Core;
using CKli.Core;
using System;

namespace CKli.CommandSample.Plugin;

/// <summary>
/// A support plugin can be in any namespace.
/// It must be sealed and specializes PluginBase.
/// </summary>
public sealed class UnusedSupportWithCommand : PluginBase
{
    public UnusedSupportWithCommand( World world )
        : base( world )
    {
    }

    [Description( "This command is implemented on an unused Support plugin." )]
    [CommandPath( "test unused" )]
    public bool UnreachableCommandHandler( IActivityMonitor monitor )
    {
        monitor.Info( "test unused cannot be called." );
        Console.WriteLine( "test unused cannot be called." );
        return true;
    }

}
