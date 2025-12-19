using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliTagDelete : Command
{
    public CKliTagDelete()
        : base( null,
                "tag delete",
                """
                Deletes local tags (and/or optionally from the remote "origin").
                The tags may not exist, this is idempotent.
                """,
                [("tag names", "One or more tag names to delete.")],
                [],
                [
                    (["--with-remote"], "Delete tags from both local and remote."),
                    (["--remote-only"], "Delete remote tags only, keeps local ones."),
                    (["--allow-multi-repo"], """
                                            Proceed even if the current path is above multiple Repos.
                                            By default, the current path must be in a Repo.
                                            """)
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool withRemote = cmdLine.EatFlag( "--with-remote" );
        bool remoteOnly = cmdLine.EatFlag( "--remote-only" );
        bool multiRepo = cmdLine.EatFlag( "--allow-multi-repo" );
        var tagNames = cmdLine.EatRemainingArgument();
        if( tagNames.Count == 0 )
        {
            monitor.Error( "Expecting at least one tag name to delete." );
            return ValueTask.FromResult( false );
        }
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && DeleteTags( monitor, this, context, tagNames, multiRepo, remoteOnly, withRemote ) );
    }

    static bool DeleteTags( IActivityMonitor monitor,
                            Command command,
                            CKliEnv context,
                            List<string> tagNames,
                            bool multiRepo,
                            bool remoteOnly,
                            bool withRemote )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                context,
                                                out var stack,
                                                out var world,
                                                skipPullStack: true ) )
        {
            return false;
        }
        var s = context.Screen.ScreenType;
        try
        {
            world.SetExecutingCommand( command );
            var repos = world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
            if( repos == null ) return false;
            if( repos.Count > 1 && !multiRepo )
            {
                monitor.Error( $"""
                    Cannot proceed on '{repos.Select( r => r.DisplayPath.Path ).Concatenate("', '")}' repositories.
                    Please specify --allow-multi-repo flag to allow deleting tags across more than one Repo at a time.
                    """ );
                return false;
            }
            bool success = true;
            foreach( var repo in repos )
            {
                if( !remoteOnly )
                {
                    success &= repo.GitRepository.DeleteLocalTags( monitor, tagNames );
                }
                if( withRemote || remoteOnly )
                {
                    success &= repo.GitRepository.DeleteRemoteTags( monitor, tagNames );
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
