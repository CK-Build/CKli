using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliRepoRemove : Command
{
    public CKliRepoRemove()
        : base( null,
                "repo remove",
                "Removes a repository from the current world.",
                [("nameOrUrl", "Name or url of the repository to remove.")],
                [],
                [
                    (["--allow-lts"], "Allows the current world to be a Long Term Support world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CommandCommonContext context,
                                                                    CommandLineArguments cmdLine )
    {
        string nameOrUrl = cmdLine.EatArgument();
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        return ValueTask.FromResult( cmdLine.CheckNoRemainingArguments( monitor )
                                     && RepoRemove( monitor, context, nameOrUrl, allowLTS ) );
    }

    static bool RepoRemove( IActivityMonitor monitor,
                            CommandCommonContext context,
                            string nameOrUrl,
                            bool allowLTS = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return CKliRepoAdd.RequiresAllowLTS( monitor, world.Name );
            }
            return world.RemoveRepository( monitor, nameOrUrl );
        }
        finally
        {
            stack.Dispose();
        }
    }

}
