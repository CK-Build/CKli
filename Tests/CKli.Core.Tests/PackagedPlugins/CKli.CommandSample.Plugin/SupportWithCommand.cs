using CK.Core;
using CKli.Core;
using System;

namespace CKli.CommandSample.Plugin;

/// <summary>
/// A support plugin can be in any namespace.
/// It must be sealed and specializes PluginBase.
/// </summary>
public sealed class SupportWithCommand : PluginBase
{
    public SupportWithCommand( World world )
        : base( world )
    {
    }

    [Description( "Writes the world name, optionnaly in upper case." )]
    [FullCommandPath( "test get-world-name" )]
    public bool NameDoesntMatter( IActivityMonitor monitor,
                                  [Description("Writes the world name in lower case.")]
                                  [OptionName("--to-lower, -l")]
                                  bool lowerCase )
    {
        var m = lowerCase ? World.Name.FullName.ToLowerInvariant() : World.Name.FullName;
        monitor.Info( $"get-world-name: {m}" );
        Console.WriteLine( m );
        return true;
    }

}
