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

    [Description( "Writes the world name, optionally in lower case." )]
    [CommandPath( "test get-world-name" )]
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

    [Description( "Command can have 0 arguments." )]
    [CommandPath( "test no-argument-at-all" )]
    public bool CommandWithNoArgument( IActivityMonitor monitor )
    {
        monitor.Info( $"CommandWithNoArgument!!!" );
        return true;
    }

    [Description( "Command can have only CKliEnv arguments." )]
    [CommandPath( "test cklienv-only" )]
    public bool CommandWithCKliEnvOnly( IActivityMonitor monitor, CKliEnv theContext )
    {
        monitor.Info( $"CommandWithCKliEnvOnly!!!" );
        return true;
    }

    [Description( "Command can directly handle CommandLineArguments. The CommandLineArguments must be closed!" )]
    [CommandPath( "test command-line" )]
    public bool CommandWithCommandLineArgumentsOnly( IActivityMonitor monitor, CommandLineArguments cmd )
    {
        if( !cmd.EatFlag( "--forget-close" ) )
        {
            if( !cmd.Close( monitor ) ) return false;
        }
        monitor.Info( $"CommandWithCommandLineArgumentsOnly!!!" );
        return true;
    }

    [Description( "Command can directly handle CommandLineArguments and CKliEnv. The CommandLineArguments must be closed!" )]
    [CommandPath( "test command-line-env" )]
    public bool CommandWithCommandLineArgumentsAndCKliEnv( IActivityMonitor monitor, CommandLineArguments cmdLine, CKliEnv context )
    {
        if( !cmdLine.EatFlag( "--forget-close" ) )
        {
            if( !cmdLine.Close( monitor ) ) return false;
        }
        monitor.Info( $"CommandWithCommandLineArgumentsAndCKliEnv!!!" );
        return true;
    }

    [Description( "Command can directly handle CommandLineArguments and CKliEnv in any order. The CommandLineArguments must be closed!" )]
    [CommandPath( "test command-env-line" )]
    public bool CommandWithCKliEnvAndCommandLineArguments( IActivityMonitor monitor, CKliEnv context, CommandLineArguments cmdLine )
    {
        if( !cmdLine.EatFlag( "--forget-close" ) )
        {
            if( !cmdLine.Close( monitor ) ) return false;
        }
        monitor.Info( $"CommandWithCKliEnvAndCommandLineArguments!!!" );
        return true;
    }

}
