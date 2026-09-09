using CK.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Create (stack) command.
/// </summary>
sealed class CKliLTSCreate : Command
{
    internal CKliLTSCreate()
        : base( null,
                "lts create",
                "Creates a new Long-Term-Support World from the current default World.",
                [("ltsName", $"The LTS name. {WorldDefinitionFile.InvalidLTSNameMessage}")],
                [],
                [] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        string ltsName = cmdLine.EatArgument();
        if( !WorldName.IsValidLTSName( ltsName ) )
        {
            monitor.Error( $"""
                Invalid LTS name '{ltsName}'.
                {WorldDefinitionFile.InvalidLTSNameMessage}
                """ );
            return ValueTask.FromResult( false );
        }
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        return new ValueTask<bool>( CreateLTSFromCurrentWorldAsync( monitor, this, context, ltsName, scopeAlive ) );
    }

    static async Task<bool> CreateLTSFromCurrentWorldAsync( IActivityMonitor monitor, Command command, CKliEnv context, string ltsName, CancellationToken scopeAlive )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: false ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command, scopeAlive );
            if( !world.Name.IsDefaultWorld )
            {
                monitor.Error( $"A Long-Term-Support world can only be created from a default World. Current world is '{world.Name}'." );
                return false;
            }
            if( !await world.CreateLTSAsync( monitor, context, ltsName ).ConfigureAwait( false ) )
            {
                return false;
            }
            // Only save the default World definition on success.
            return stack.Close( monitor );
        }
        finally
        {
            stack.Dispose();
        }
    }
}
