using CK.Core;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Stack info command - displays information about the current stack.
/// </summary>
sealed class CKliStackInfo : Command
{
    internal CKliStackInfo()
        : base( null,
                "stack info",
                "Displays Stack information: remote URL, current branch, and commit status.",
                [], [], [] )
    {
    }

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        if( !cmdLine.Close( monitor ) )
        {
            return ValueTask.FromResult( false );
        }
        return ValueTask.FromResult( DisplayStackInfo( monitor, this, context ) );
    }

    static bool DisplayStackInfo( IActivityMonitor monitor, Command command, CKliEnv context )
    {
        var stack = StackRepository.TryOpenFromPath( monitor, context, out bool error, skipPullStack: true );
        if( error ) return false;
        if( stack == null )
        {
            monitor.Error( "Not in a Stack directory." );
            return false;
        }

        try
        {
            var git = stack.GitRepository;
            var screenType = context.Screen.ScreenType;

            var info = screenType.Unit
                .AddBelow( screenType.Text( $"Stack: {stack.StackName}" ) )
                .AddBelow( screenType.Text( $"Path: {stack.StackWorkingFolder}" ) )
                .AddBelow( screenType.Text( $"Remote: {git.RepositoryKey.OriginUrl}" ) )
                .AddBelow( screenType.Text( $"Branch: {git.CurrentBranchName}" ) )
                .AddBelow( screenType.Text( $"Public: {stack.IsPublic}" ) );

            context.Screen.Display( info );
            return stack.Close( monitor );
        }
        finally
        {
            stack.Dispose();
        }
    }
}
