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
        return RepositoryAddOrCreateAsync( monitor, this, context, cmdLine, create: false );
    }

    internal static async ValueTask<bool> RepositoryAddOrCreateAsync( IActivityMonitor monitor,
                                                                      Command command,
                                                                      CKliEnv context,
                                                                      CommandLineArguments cmdLine,
                                                                      bool create )
    {
        string sUrl = cmdLine.EatArgument();
        if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var repositoryUrl ) )
        {
            monitor.Error( $"Invalid <repositoryUrl> argument '{sUrl}'. It must be an absolute url." );
            return false;
        }
        bool allowLTS = cmdLine.EatFlag( "--allow-lts" );
        if( !cmdLine.Close( monitor )
            || !StackRepository.OpenWorldFromPath( monitor, context, out var stack, out var world, skipPullStack: true ) )
        {
            return false;
        }
        try
        {
            world.SetExecutingCommand( command );
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return RequiresAllowLTS( monitor, world.Name );
            }
            var gitKey = GitRepositoryKey.Create( monitor, context.SecretsStore, repositoryUrl, stack.IsPublic );
            if( gitKey == null )
            {
                return false;
            }
            if( gitKey.IsStackRepository )
            {
                monitor.Error( $"Cannot add a '-Stack' repository to a World." );
                return false;
            }
            GitHostingProvider? hostingProvider = null;
            NormalizedPath remoteRepoPath = default;
            if( create )
            {
                if( !gitKey.TryGetHostingInfo( monitor, out hostingProvider, out remoteRepoPath ) )
                {
                    return false;
                }
                var remoteInfo = await hostingProvider.CreateRepositoryAsync( monitor, remoteRepoPath, !stack.IsPublic ).ConfigureAwait( false );
                if( remoteInfo == null )
                {
                    return false;
                }
            }
            // AddRepository handles the WorldDefinition file save and commit.
            bool success = await world.AddRepositoryAsync( monitor, gitKey, context.CurrentDirectory ).ConfigureAwait( false );
            // On error, compensate by deleting the new repository.
            if( !success && hostingProvider != null )
            {
                await hostingProvider.DeleteRepositoryAsync( monitor, remoteRepoPath ).ConfigureAwait( false );
            }
            return success;
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
