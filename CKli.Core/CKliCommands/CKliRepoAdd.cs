using CK.Core;
using CKli.Core;
using System;
using System.Threading.Tasks;

namespace CKli;

sealed class CKliRepoAdd : Command
{
    public CKliRepoAdd()
        : base( null,
                "repo add",
                "Adds a repository to the current world. The repository is cloned in the current directory.",
                [("repositoryUrl", "Url of the repository to add to the current World.")],
                [],
                [
                    (["--allow-lts"], "Allows the current world to be a Long Term Support world.")
                ] )
    {
    }

    protected internal override ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                    CKliEnv context,
                                                                    CommandLineArguments cmdLine )
    {
        string sUrl = cmdLine.EatArgument();
        if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var uri ) )
        {
            monitor.Error( $"Invalid <repositoryUrl> argument '{sUrl}'. It must be an absolute url." );
            return ValueTask.FromResult( false );
        }
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        return ValueTask.FromResult( cmdLine.Close( monitor )
                                     && RepositoryAdd( monitor, context, uri, allowLTS ) );
    }

    static bool RepositoryAdd( IActivityMonitor monitor,
                               CKliEnv context,
                               Uri repositoryUrl,
                               bool allowLTS )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return RequiresAllowLTS( monitor, world.Name );
            }
            // AddRepository handles the WorldDefinition file save and commit.
            return world.AddRepository( monitor, repositoryUrl, context.CurrentDirectory );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }

    internal static bool RequiresAllowLTS( IActivityMonitor monitor, LocalWorldName worldName )
    {
        monitor.Error( $"""
                        Current world '{worldName}' is not the default world.
                        --allow-lts option must be specified.
                        """ );
        return false;
    }


}
