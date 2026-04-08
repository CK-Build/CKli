using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliPull : Command
{
    internal CKliPull()
        : base( null,
                "pull",
                """
                Pulls the Stack repository and all Repo's local branches that track a remote branch.
                By default, tags that point to the remote branches will be retrieved and will replace locally defined tags if they point to the same object. If a local tag points to a different object, this will be an error.
                To prevent this, use 'ckli tag list' to detect conflicts.
                """,
                [],
                [],
                [
                    (["--all"], "Consider all the Repos' of the current World (even if current path is in a Repo)."),
                    (["--preserve-local-tags"], "Preserve local tags, fetching remote-only tags."),
                    (["--from-all-remotes"], "Consider all available remotes, not only 'origin'."),
                    (["--continue-on-error"], "Continues even on error. By default the first error stops the operation."),
             ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool preserveLocalTags = cmdLine.EatFlag( "--preserve-local-tags" );
        bool fromAllRemotes = cmdLine.EatFlag( "--fromAllRemotes" );
        bool continueOnError = cmdLine.EatFlag( "--continue-on-error" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Pull( monitor, this, context, all, preserveLocalTags, fromAllRemotes, continueOnError ) );


        static bool Pull( IActivityMonitor monitor, Command command, CKliEnv context, bool all, bool preserveLocalTags, bool fromAllRemotes, bool continueOnError )
        {
            if( !StackRepository.OpenWorldFromPath( monitor,
                                                    context,
                                                    out var stack,
                                                    out var world,
                                                    skipPullStack: false ) )
            {
                return false;
            }
            try
            {
                world.SetExecutingCommand( command );
                var repos = all
                            ? world.GetAllDefinedRepo( monitor )
                            : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
                if( repos == null ) return false;

                bool success = DoPull( monitor, fromAllRemotes, continueOnError, repos, preserveLocalTags ? WithTag.RemoteOnly : WithTag.PullAll );
                // Save a dirty World's DefinitionFile ony if no unhandled exception is thrown.
                return stack.Close( monitor ) && success;
            }
            finally
            {
                // On error, don't save a dirty World's DefinitionFile.
                stack.Dispose();
            }
        }
    }

    internal enum WithTag { None, PullAll, RemoteOnly };

    internal static bool DoPull( IActivityMonitor monitor,
                                 bool fromAllRemotes,
                                 bool continueOnError,
                                 IReadOnlyList<Repo> repos,
                                 WithTag withTags )
    {
        bool success = true;
        // To limit roundtrips to the remotes, we fetch all the remote branches at once
        // and then use MergeTrackedBranches to merge them (MergeTrackedBranches handles the branch
        // that is checked out correctly).
        using( monitor.OpenInfo( $"Fetching remote branches {(withTags == WithTag.PullAll ? "and tags " : "")}for {repos.Count} repositories." ) )
        {
            foreach( var repo in repos )
            {
                success &= repo.GitRepository.FetchRemoteBranches( monitor, withTags == WithTag.PullAll, !fromAllRemotes );
                if( !success && !continueOnError ) break;
                if( withTags == WithTag.RemoteOnly )
                {
                    success &= repo.GitRepository.FetchTags( monitor );
                }
                if( !success && !continueOnError ) break;
            }
        }
        if( success || continueOnError )
        {
            using( monitor.OpenInfo( $"Merging remote branches into existing local ones for {repos.Count} repositories." ) )
            {
                foreach( var repo in repos )
                {
                    success &= repo.GitRepository.MergeTrackedBranches( monitor, continueOnError, fromAllRemotes );
                }
            }
        }

        return success;
    }
}
