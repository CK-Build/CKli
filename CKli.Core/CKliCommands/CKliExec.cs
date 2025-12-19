using CK.Core;
using System.Collections.Generic;
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
                The <process-name-and-args> contains the whole command line, for example: 'ckli exec dotnet test'.
                """,
                [("process-name-and-args", "Name of the process to run and its arguments.")],
                [],
                [(["--ckli-continue-on-error"], "Continue despite of the process returning a non 0 exit code."),
                 (["--ckli-all"], "Consider all the Repos of the current World (even if current path is in a Repo).")] )
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
        bool continueOnError = cmdLine.EatFlag( "--ckli-continue-on-error" );
        bool all = cmdLine.EatFlag( "--ckli-all" );
        cmdLine.CloseWithRemainingAsProcessStartArgs( out var arguments );
        return ValueTask.FromResult( DoRun( monitor, this, context, all, continueOnError, processName, arguments ) );
    }

    static bool DoRun( IActivityMonitor monitor, Command command, CKliEnv context, bool all, bool continueOnError, string processName, string arguments )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command );
            IReadOnlyList<Repo>? repos = all
                                          ? world.GetAllDefinedRepo( monitor )
                                          : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
            if( repos == null )
            {
                return false;
            }
            foreach( var repo in repos )
            {
                using( monitor.OpenTrace( $"Executing '{processName} {arguments}' in '{repo.DisplayPath}'." ) )
                {
                    var exitCode = ProcessRunner.RunProcess( monitor.ParallelLogger, processName, arguments, repo.WorkingFolder, null );
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
            // Consider that the final result requires no error when saving a dirty World's DefinitionFile.
            return stack.Close( monitor );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }
}
