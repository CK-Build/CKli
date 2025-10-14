using CK.Core;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Dotnet command.
/// </summary>
sealed class CKliDotNet : Command
{
    internal CKliDotNet()
        : base( null,
                "dotnet",
                """
                Executes dotnet command line on each Repo.
                The remaining aguments are directly the one of the .NET CLI. See https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet. 
                """,
                [], [],
                [(["--continue-on-error"], "Continue despite of dotnet process returning a non 0 exit code.")] )
    {
    }

    internal protected override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool continueOnError = cmdLine.EatFlag( "--continue-on-error" );
        cmdLine.CloseWithRemainingAsProcessStartArgs( out var arguments );
        return ValueTask.FromResult( DoRun( monitor, context, continueOnError, arguments ) );
    }

    static bool DoRun( IActivityMonitor monitor, CKliEnv context, bool continueOnError, string arguments )
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
                using( monitor.OpenTrace( $"Executing 'dotnet {arguments}' in '{repo.DisplayPath}'." ) )
                {
                    var exitCode = ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", arguments, repo.WorkingFolder, null );
                    if( exitCode != 0 )
                    {
                        var msg = $"dotnet failed in '{repo.DisplayPath}' with exit code {exitCode}. Use 'ckli log' to see the logs.";
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
