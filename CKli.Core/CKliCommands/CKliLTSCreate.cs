using CK.Core;
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
                [("ltsName", "The LTS name. Must start with the '@' character and be followed by at least .")],
                [],
                [
                    (["--private"], "Specify a private Stack. By default, a Stack is public."),
                    (["--ignore-parent-stack"], "Allows the new Stack to be inside an existing one.")
       ] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                          CKliEnv context,
                                                                          CommandLineArguments cmdLine )
    {
        string ltsName = cmdLine.EatArgument();
        if( !WorldName.IsValidLTSName( ltsName ) )
        {
            monitor.Error( $"""
                Invalid LTS name '{ltsName}'.
                Must be at least 3 characters that starts with '@', only ASCII lowercase characters, digits, - (hyphen), _ (underscore) and '.' (dot).
                """ );
            return ValueTask.FromResult( false );
        }
        return CreateLTSAsync( monitor, this, context, ltsName );
    }

    static async ValueTask<bool> CreateLTSAsync( IActivityMonitor monitor, Command command, CKliEnv context, string ltsName )
    {
        if( !await CreateLTSFromCurrentWorldAsync( monitor, command, context, ltsName ).ConfigureAwait( false ) )
        {
            return false; 
        }
        
        return true;
    }

    static async Task<bool> CreateLTSFromCurrentWorldAsync( IActivityMonitor monitor, Command command, CKliEnv context, string ltsName )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: false ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command );
            if( !world.Name.IsDefaultWorld )
            {
                monitor.Error( $"A Long-Term-Support world can only be created from a default World. Current world is '{world.Name}'." );
                return false;
            }
            if( !await world.CreateLTSAsync( monitor, context, ltsName ).ConfigureAwait( false ) )
            {
                return false;
            }
            return stack.Close( monitor );
        }
        finally
        {
            stack.Dispose();
        }
    }
}
