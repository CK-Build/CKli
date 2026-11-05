using CK.Core;
using CKli.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli;

/// <summary>
/// Adds an existing remote to the world.
/// </summary>
public sealed class CKliRepoAdd : Command
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
                                                                    CommandLineArguments cmdLine,
                                                                    CancellationToken scopeAlive )
    {
        return RepositoryAddOrCreateAsync( monitor, this, context, cmdLine, create: false, scopeAlive );
    }

    internal static async ValueTask<bool> RepositoryAddOrCreateAsync( IActivityMonitor monitor,
                                                                      Command command,
                                                                      CKliEnv context,
                                                                      CommandLineArguments cmdLine,
                                                                      bool create,
                                                                      CancellationToken scopeAlive )
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
            world.SetExecutingCommand( command, scopeAlive );
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
            // The plugins can set the default branch (the root branch of a branch model).
            var adding = new RepoAddingEventArgs( monitor, context, world, gitKey.OriginUrl, create );
            if( world.Events._repoAddingEventSender.HasHandlers
                && !await world.Events._repoAddingEventSender.SafeRaiseAsync( monitor, adding ).ConfigureAwait( false ) )
            {
                return false;
            }
            var defaultBranchName = adding.DefaultBranchName;
            GitHostingProvider? hostingProvider = null;
            NormalizedPath remoteRepoPath = default;
            if( create )
            {
                if( !gitKey.TryGetHostingInfo( monitor, out hostingProvider, out remoteRepoPath ) )
                {
                    return false;
                }
                var remoteInfo = await hostingProvider.CreateRepositoryAsync( monitor,
                                                                              remoteRepoPath,
                                                                              !stack.IsPublic,
                                                                              defaultBranchName,
                                                                              scopeAlive ).ConfigureAwait( false );
                if( remoteInfo == null )
                {
                    return false;
                }
            }
            // AddRepository handles the WorldDefinition file save and commit.
            bool success = await world.AddRepositoryAsync( monitor, context, gitKey, defaultBranchName ).ConfigureAwait( false );
            if( hostingProvider == null ) return success;
            // On error, compensate by deleting the new repository (when create was true, the hostingProvider is not null).
            if( !success )
            {
                await hostingProvider.DeleteRepositoryAsync( monitor, remoteRepoPath, cancellation: default ).ConfigureAwait( false );
                return false;
            }
            return await PushDefaultBranchAsync( monitor, world, gitKey, hostingProvider, remoteRepoPath, defaultBranchName, scopeAlive ).ConfigureAwait( false );
        }
        finally
        {
            // On error, don't save a dirty World's DefinitionFile.
            stack.Dispose();
        }
    }

    // A created remote repository may be empty (GitHub creates it without an initial commit): a host then takes the
    // first pushed branch as the default one, whatever it is. The default branch is pushed now and set as the default one
    // (hosts that create an initial commit use the default branch name given at creation).
    static async Task<bool> PushDefaultBranchAsync( IActivityMonitor monitor,
                                                    World world,
                                                    GitRepositoryKey gitKey,
                                                    GitHostingProvider hostingProvider,
                                                    NormalizedPath remoteRepoPath,
                                                    string defaultBranchName,
                                                    CancellationToken cancellation )
    {
        var repo = world.GetDefinedRepo( monitor, gitKey.OriginUrl.ToString() );
        if( repo == null ) return false;
        var git = repo.GitRepository;
        var branch = git.Repository.Branches[defaultBranchName];
        if( branch == null )
        {
            monitor.Warn( $"Branch '{defaultBranchName}' doesn't exist in '{repo.DisplayPath}': the default branch of the remote repository is not set." );
            return true;
        }
        if( !git.PushBranch( monitor, branch, autoCreateRemoteBranch: true ) )
        {
            monitor.Error( $"Unable to push the default branch '{defaultBranchName}' of the new repository '{repo.DisplayPath}'. It is added to the World: push it with 'ckli push'." );
            return false;
        }
        if( hostingProvider.HasDefaultBranch
            && !await hostingProvider.SetDefaultBranchAsync( monitor, remoteRepoPath, defaultBranchName, LogLevel.Warn, cancellation ).ConfigureAwait( false ) )
        {
            monitor.Warn( $"Unable to set '{defaultBranchName}' as the default branch of the remote repository '{repo.DisplayPath}'." );
        }
        return true;
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
