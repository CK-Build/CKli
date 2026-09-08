using CK.Core;
using CKli.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliWorldReferenceRemove : Command
{
    public CKliWorldReferenceRemove()
        : base( null,
                "world reference remove",
                """
                Removes a <Reference /> element from the current world.
                Removing a reference that doesn't exist is not an error.
                """,
                [("nameOrUrl", "Url of the referenced Stack, its repository name (\"XXX-Stack\") or its stack name (\"XXX\").")],
                [],
                [
                    (["--allow-lts"], "Allows the current world to be a Long Term Support world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        string nameOrUrl = cmdLine.EatArgument();
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && RemoveReference( monitor, context, nameOrUrl, allowLTS ) );
    }

    static bool RemoveReference( IActivityMonitor monitor, CKliEnv context, string nameOrUrl, bool allowLTS )
    {
        if( !StackRepository.OpenFromPath( monitor, context, out var stack, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            var worldName = stack.GetWorldNameFromPath( monitor, context.CurrentDirectory );
            if( worldName == null )
            {
                return false;
            }
            if( !allowLTS && !worldName.IsDefaultWorld )
            {
                return CKliRepoAdd.RequiresAllowLTS( monitor, worldName );
            }
            var definitionFile = worldName.LoadDefinitionFile( monitor );
            if( definitionFile == null )
            {
                return false;
            }
            // RemoveReference handles the WorldDefinition file save and commit.
            return definitionFile.RemoveReference( monitor, nameOrUrl );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }
}
