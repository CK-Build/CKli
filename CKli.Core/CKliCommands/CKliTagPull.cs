using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliTagPull : Command
{
    public CKliTagPull()
        : base( null,
                "tag pull",
                """
                Pulls the specified tags from the remote "origin" in the current Repo.
                Local modifications of fetched tags are lost, conflicts are solved: remote always wins. Use "ckli tag list" to analyze tags.  
                """,
                [("tag names", """One or more tag names to pull. * wildcard can be used to pull all the tags.""")],
                [],
                [
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
        return ValueTask.FromResult( PullOrPushTags( monitor, this, context, cmdLine, pull: true ) );
    }

    internal static bool PullOrPushTags( IActivityMonitor monitor,
                                         Command command,
                                         CKliEnv context,
                                         CommandLineArguments cmdLine,
                                         bool pull )
    {
        bool multiRepo = cmdLine.EatFlag( "--allow-multi-repo" );
        IEnumerable<string> tagNames = cmdLine.EatRemainingArgument();
        var invalidNames = tagNames.Where( n => !GitRepository.IsCKliValidTagName( n ) );
        if( invalidNames.Any() )
        {
            monitor.Warn( $"""
                    Ignored tag names: '{invalidNames.Concatenate( "', '" )}'.
                    To avoid case sensitivity nightmare, CKli only handles tag names containing
                    ascii characters in which letters must be lowercase.
                    """ );
            tagNames = tagNames.Except( invalidNames );
        }
        if( !tagNames.Any() )
        {
            monitor.Error( $"Expecting at least one tag name to {(pull ? "pull" : "push")}." );
            return false;
        }
        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }
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
            var repos = CKliTagDelete.GetRepos( monitor, context, multiRepo, world );
            if( repos == null ) return false;

            bool success = true;
            foreach( var repo in repos )
            {
                success &= pull
                            ? repo.GitRepository.PullTags( monitor, tagNames )
                            : repo.GitRepository.PushTags( monitor, tagNames );
            }
            return success;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
