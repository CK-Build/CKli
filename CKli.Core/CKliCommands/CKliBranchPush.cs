//using CK.Core;
//using CKli.Core;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;

//namespace CKli;

// Not required (for the moment).

//sealed class CKliBranchPush : Command
//{
//    public CKliBranchPush()
//        : base( null,
//                "branch push",
//                """
//                Pushes the specified branch to its remote "origin" (creating it if it doesn't exist yet).
//                When applied to multiple Repo, a warning is emitted if the branch doesn't exist.
//                The branch is fetched and must be successfully merged for the push to succeed.
//                """,
//                [("branch", "The branch name to push.")],
//                [],
//                [
//                    (["--all"], "Consider all the Repos' of the current World (even if current path is in a Repo).")
//                ] )
//    {
//    }

//    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
//                                                                    CKliEnv context,
//                                                                    CommandLineArguments cmdLine )
//    {
//        string branchName = cmdLine.EatArgument();
//        bool all = cmdLine.EatFlag( "--all" );
//        return ValueTask.FromResult( cmdLine.Close( monitor )
//                                     && PushBranch( monitor, this, context, branchName, all ) );
//    }

//    internal static bool PushBranch( IActivityMonitor monitor,
//                                     Command command,
//                                     CKliEnv context,
//                                     string branchName,
//                                     bool all )
//    {

//        if( !StackRepository.OpenWorldFromPath( monitor,
//                                                context,
//                                                out var stack,
//                                                out var world,
//                                                skipPullStack: true ) )
//        {
//            return false;
//        }
//        var s = context.Screen.ScreenType;
//        try
//        {
//            world.SetExecutingCommand( command );
//            var repos = all
//                        ? world.GetAllDefinedRepo( monitor )
//                        : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
//            if( repos == null ) return false;

//            foreach( var repo in repos )
//            {
//                if( !repo.GitRepository.FetchRemoteBranch( monitor, branchName, withTags: false, out var branch ) )
//                {
//                    return false; 
//                }
//                if( branch == null )
//                {
//                    monitor.Warn( $"Branch '{branchName}' not found in '{repo.DisplayPath}'. Push skipped." );
//                }
//                else
//                {
//                    if( branch.IsTracking )
//                    {
//                        if( !repo.GitRepository.MergeTrackedBranch( monitor, ref branch ) )
//                        {
//                            return false;
//                        }
//                    }
//                    if( !repo.GitRepository.PushBranch( monitor, branch, autoCreateRemoteBranch: true ) )
//                    {
//                        return false;
//                    }
//                }
//            }
//            return true;
//        }
//        finally
//        {
//            stack.Dispose();
//        }
//    }
//}
