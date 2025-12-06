using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliFetch : Command
{
    public CKliFetch()
        : base( null,
                "fetch",
                """
                Fetches all branches and tags of the current Repo or all the Repos of the current World.
                When --without-tags is not specified, locally modified tags are lost.
                """,
                [],
                [],
                [
                    (["--all"], "Fetch from all the Repos of the current World (even if current path is in a Repo)."),
                    (["--without-tags"], "Don't fetch tags: locally modified tags are preserved."),
                    (["--from-all-remotes"], "Fetch from all available remotes, not only from 'origin'.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        bool all = cmdLine.EatFlag( "--all" );
        bool withoutTags = cmdLine.EatFlag( "--without-tags" );
        bool fromAllRemotes = cmdLine.EatFlag( "--from-all-remotes" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && Fetch( monitor, context, all, withoutTags, fromAllRemotes ) );
    }

    static bool Fetch( IActivityMonitor monitor, CKliEnv context, bool all, bool withoutTags, bool fromAllRemotes )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                context,
                                                out var stack,
                                                out var world,
                                                skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            var repos = all
                        ? world.GetAllDefinedRepo( monitor )
                        : world.GetAllDefinedRepo( monitor, context.CurrentDirectory );
            if( repos == null ) return false;
            bool success = true;
            foreach( var r in repos )
            {
                success &= r.Fetch( monitor, withTags: !withoutTags, originOnly: !fromAllRemotes );
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
