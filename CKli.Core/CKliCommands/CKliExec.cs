using CK.Core;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Executes an external process on each Repo of the current World.
/// </summary>
sealed class CKliExec : Command
{
    internal CKliExec()
        : base( null,
                "exec",
                """
                Executes an external process on each Repo (the working directory is the repository working folder).
                After the required <process-name> argument, the remaining arguments are the process arguments, for example: 'ckli exec dotnet test'.
                """,
                [("process-name","Name of the process to run.")],
                [],
                [(["--continue-on-error"], "Continue despite of the process returning a non 0 exit code.")] )
    {
    }

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string processName = cmdLine.EatArgument();
        if( string.IsNullOrWhiteSpace( processName ) )
        {
            monitor.Error( $"Invalid process name '{processName}'." );
            return ValueTask.FromResult( false );
        }
        bool continueOnError = cmdLine.EatFlag( "--continue-on-error" );
        cmdLine.CloseWithRemainingAsProcessStartArgs( out var arguments );
        return ValueTask.FromResult( DoRun( monitor, context, continueOnError, processName, arguments ) );
    }

    static bool DoRun( IActivityMonitor monitor, CKliEnv context, bool continueOnError, string processName, string arguments )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            var repos = world.GetAllDefinedRepo( monitor );
            if( repos == null )
            {
                return false;
            }
            foreach( var repo in repos )
            {
                using( monitor.OpenTrace( $"Executing '{processName} {arguments}' in '{repo.DisplayPath}'." ) )
                {
                    var exitCode = ProcessRunner.RunProcess( ActivityMonitor.StaticLogger, processName, arguments, repo.WorkingFolder, null );
                    if( exitCode != 0 )
                    {
                        var msg = $"'{processName}' failed in Repo '{repo.DisplayPath}' with exit code {exitCode}. Use 'ckli log' to see the logs.";
                        if( continueOnError )
                        {
                            monitor.Warn( msg );
                        }
                        else
                        {
                            monitor.Error( msg );
                            return false;
                        }
                    }
                    else
                    {
                        monitor.CloseGroup( "Success." );
                    }
                }
            }
            return true;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
