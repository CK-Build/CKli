using CK.Core;
using CKli.Core;
using System.Collections.Generic;
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
                Local modifications of fetched tags are lost. 
                """,
                [("tag names", "One or more tag names to pull.")],
                [],
                [] )
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
            var repo = world.GetDefinedRepo( monitor, context.CurrentDirectory );
            if( repo == null ) return false;
            return pull
                    ? repo.GitRepository.PullTags( monitor, tagNames )
                    : repo.GitRepository.PushTags( monitor, tagNames );
        }
        finally
        {
            stack.Dispose();
        }
    }
}
