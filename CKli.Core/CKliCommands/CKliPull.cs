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
                By default, all remote tags are only "fetched": local tags are preserved. When --with-tags is specified, remote tags overwrite local ones.
                Use 'ckli tag list' to analyze local/remote and conflicting tags.
                """,
                [],
                [],
                [
                    (["--all"], "Consider all the Repos' of the current World (even if current path is in a Repo)."),
                    (["--with-tags"], "Pull tags: remote tags replace local ones with the same name."),
                    (["--continue-on-error"], "Continues even on error. By default the first error stops the operation."),
             ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool withTags = cmdLine.EatFlag( "--with-tags" );
        bool continueOnError = cmdLine.EatFlag( "--continue-on-error" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Pull( monitor, this, context, all, withTags, continueOnError ) );


        static bool Pull( IActivityMonitor monitor, Command command, CKliEnv context, bool all, bool withTags, bool continueOnError )
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

                bool success = DoPull( monitor, continueOnError, repos, withTags );
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

    internal static bool DoPull( IActivityMonitor monitor,
                                 bool continueOnError,
                                 IReadOnlyList<Repo> repos,
                                 bool withTags )
    {
        bool success = true;
        // To limit roundtrips to the remotes, we fetch all the remote branches at once
        // and then use MergeTrackedBranches to merge them (MergeTrackedBranches handles the branch
        // that is checked out correctly).
        using( monitor.OpenInfo( $"Fetching remote branches {(withTags ? "and tags " : "")}for {repos.Count} repositories." ) )
        {
            foreach( var repo in repos )
            {
                // withTags = true may set TagFetchMode.Auto (tags that are referenced by the fetched objects will be retrieved)
                // but we want all tags to be updated. So use the "pull tag *".
                success &= repo.GitRepository.FetchRemoteBranches( monitor, withTags: false );
                if( !success && !continueOnError ) break;
                if( !withTags )
                {
                    // "ckli pull" => "ckli tag fetch" => By default the tags are "safely fetched".
                    success &= repo.GitRepository.FetchTags( monitor );
                }
                else
                {
                    // "ckli pull --with-tags" => "ckli tag pull *" => git pull --tags --force
                    success &= repo.GitRepository.PullTags( monitor, ["*"] );
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
                    success &= repo.GitRepository.MergeRemoteBranches( monitor, continueOnError, fromAllRemotes: false );
                    if( !success && !continueOnError ) break;
                }
            }
        }
        return success;
    }
}
