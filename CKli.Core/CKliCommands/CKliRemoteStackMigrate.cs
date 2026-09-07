using CK.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Remote stack migrate command: moves the Stack repository to a new remote url.
/// </summary>
sealed class CKliRemoteStackMigrate : Command
{
    internal CKliRemoteStackMigrate()
        : base( null,
                "remote stack migrate",
                """
                Moves the Stack repository to a new remote url: creates the new remote repository if needed,
                changes the 'origin' url, pushes the Stack content and archives the previous repository.
                This is idempotent: running it again on a done migration checks the final state and changes
                nothing, and running it again on an interrupted one finishes the job.
                The repositories of the Worlds are not concerned: only the Stack repository moves.
                """,
                [("newUrl", "The new remote url of the Stack repository. Its name must remain '{StackName}-Stack'.")],
                [],
                [] )
    {
    }

    public override InteractiveMode InteractiveMode => InteractiveMode.Rejects;

    internal protected override async ValueTask<bool> HandleCommandAsync( IActivityMonitor monitor,
                                                                          CKliEnv context,
                                                                          CommandLineArguments cmdLine,
                                                                          CancellationToken scopeAlive )
    {
        string sUrl = cmdLine.EatArgument();
        if( !Uri.TryCreate( sUrl, UriKind.Absolute, out var newUrl ) )
        {
            monitor.Error( $"Invalid <newUrl> argument '{sUrl}'. It must be an absolute url." );
            return false;
        }
        var urlError = GitRepositoryKey.GetRepositoryUrlError( newUrl );
        if( urlError != null )
        {
            monitor.Error( urlError );
            return false;
        }
        if( !cmdLine.Close( monitor ) )
        {
            return false;
        }
        return await MigrateAsync( monitor, context, newUrl, scopeAlive ).ConfigureAwait( false );
    }

    static async ValueTask<bool> MigrateAsync( IActivityMonitor monitor,
                                               CKliEnv context,
                                               Uri newUrl,
                                               CancellationToken cancellation )
    {
        var stack = StackRepository.TryOpenFromPath( monitor, context, out bool error, skipPullStack: true );
        if( error ) return false;
        if( stack == null )
        {
            monitor.Error( $"""
                No Stack found here or above '{context.CurrentDirectory}': a '{StackRepository.PublicStackName}'
                or '{StackRepository.PrivateStackName}' folder is required.
                """ );
            return false;
        }
        try
        {
            var currentUrl = stack.GitRepository.RepositoryKey.OriginUrl;
            var newKey = GitRepositoryKey.Create( monitor, context.SecretsStore, newUrl, stack.IsPublic );
            if( newKey == null ) return false;
            // The Stack folder name and its world definition file names are the Stack name: a migration
            // cannot rename the Stack.
            if( !newKey.CheckOriginUrlStackSuffix( monitor, out var newStackName ) ) return false;
            if( !StringComparer.OrdinalIgnoreCase.Equals( newStackName, stack.StackName ) )
            {
                monitor.Error( $"""
                    Unable to migrate the Stack '{stack.StackName}' to '{newUrl}': the repository name must
                    remain '{stack.StackName}-Stack' (a migration cannot rename a Stack).
                    """ );
                return false;
            }
            bool alreadyMoved = GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer.Equals( currentUrl, newUrl );

            // 1 - The new remote repository must exist. When it doesn't, we create it: this migration then owns
            //     it and the push below can safely be forced (some providers create it with an initial commit).
            if( !newKey.TryGetHostingInfo( monitor, out var provider, out var newRepoPath ) ) return false;
            var info = await provider.GetRepositoryInfoAsync( monitor, newRepoPath, mustExist: false, cancellation )
                                     .ConfigureAwait( false );
            if( info == null ) return false;
            bool created = false;
            if( info.Exists )
            {
                monitor.Info( $"Repository '{newUrl}' already exists." );
            }
            else
            {
                info = await provider.CreateRepositoryAsync( monitor,
                                                             newRepoPath,
                                                             isPrivate: !stack.IsPublic,
                                                             defaultBranchName: stack.GitRepository.CurrentBranchName,
                                                             cancellation ).ConfigureAwait( false );
                if( info == null ) return false;
                created = true;
            }

            // 2 - Change the 'origin' url, unless a previous run already did it.
            Uri? previousUrl;
            if( alreadyMoved )
            {
                monitor.Info( $"The Stack remote url is already '{newUrl}'." );
                // Null when nothing is pending: the migration is done, this run only checks the final state.
                previousUrl = stack.MigrationSourceUrl;
            }
            else
            {
                // A pending migration must reach its final state before we forget where we come from:
                // SetRemoteUrl overwrites the MigrationSourceUrl below.
                var pendingUrl = stack.MigrationSourceUrl;
                if( pendingUrl != null
                    && !(await ArchiveAndForgetAsync( monitor, context, stack, pendingUrl, cancellation ).ConfigureAwait( false )) )
                {
                    return false;
                }
                previousUrl = currentUrl;
                // This records the previousUrl as the MigrationSourceUrl.
                if( !stack.SetRemoteUrl( monitor, newUrl, push: false ) ) return false;
                // The credentials come from the key the repository has been opened with: the Stack must be
                // reopened for the push (and the archive) to use the ones of the new url.
                stack.Dispose();
                stack = StackRepository.TryOpenFromPath( monitor, context, out error, skipPullStack: true );
                if( stack == null )
                {
                    if( !error ) monitor.Error( $"Unable to reopen the Stack at '{context.CurrentStackPath}'." );
                    return false;
                }
            }

            // 3 - Push the content. The push is forced when this migration owns the target repository: we have
            //     just created it, or we are (re)doing the move onto it.
            if( !stack.PushChanges( monitor, force: created || previousUrl != null ) ) return false;

            // 4 - The previous repository can now reach its final state.
            if( previousUrl != null
                && !(await ArchiveAndForgetAsync( monitor, context, stack, previousUrl, cancellation ).ConfigureAwait( false )) )
            {
                return false;
            }
            return stack.Close( monitor );
        }
        finally
        {
            stack?.Dispose();
        }
    }

    /// <summary>
    /// Archives the previous repository (when it still exists, is not already archived and its provider can do it)
    /// and forgets it: the <see cref="StackRepository.MigrationSourceUrl"/> is cleared once this final state is
    /// reached, so that another run has nothing left to do.
    /// </summary>
    static async ValueTask<bool> ArchiveAndForgetAsync( IActivityMonitor monitor,
                                                        CKliEnv context,
                                                        StackRepository stack,
                                                        Uri previousUrl,
                                                        CancellationToken cancellation )
    {
        using( monitor.OpenInfo( $"Archiving the previous Stack repository '{previousUrl}'." ) )
        {
            var key = GitRepositoryKey.Create( monitor, context.SecretsStore, previousUrl, stack.IsPublic );
            if( key == null || !key.TryGetHostingInfo( monitor, out var provider, out var repoPath ) )
            {
                return false;
            }
            var info = await provider.GetRepositoryInfoAsync( monitor, repoPath, mustExist: false, cancellation )
                                     .ConfigureAwait( false );
            if( info == null ) return false;
            if( !info.Exists )
            {
                monitor.Info( "Repository doesn't exist anymore: nothing to archive." );
            }
            else if( info.IsArchived )
            {
                monitor.Info( "Repository is already archived." );
            }
            else if( !provider.CanArchiveRepository )
            {
                monitor.Warn( $"""
                    The '{provider.ProviderType}' hosting provider cannot archive a repository: '{previousUrl}'
                    is left as-is and should be archived or deleted manually.
                    """ );
            }
            else if( !await provider.ArchiveRepositoryAsync( monitor, repoPath, archive: true, cancellation )
                                    .ConfigureAwait( false ) )
            {
                return false;
            }
            return stack.SetMigrationSourceUrl( monitor, null );
        }
    }
}
