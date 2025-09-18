using ConsoleAppFramework;
using System;

namespace CKli;

[RegisterCommands]
public sealed class RootCommands
{
    /// <summary>
    /// Clones a Stack and all its current World repositories in the current directory.
    /// </summary>
    /// <param name="stackUrl">The url stack repository to clone from. The repository name must end with '-Stack'.</param>
    /// <param name="private">Indicates a private repository. A Personal Access Token (or any other secret) is required.</param>
    /// <param name="allowDuplicate">Allows a Stack that already exists locally to be cloned.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Clone( [Argument, AbsoluteUrlParser] AbsoluteUrl stackUrl,
                      bool @private = false,
                      bool allowDuplicate = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.Clone( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, stackUrl.Url, @private, allowDuplicate );
        } );
    }

    /// <summary>
    /// Resynchronizes the current Repo or World from the remotes.
    /// </summary>
    /// <param name="all">Pull all the Repos of the current World (even if current path is in a Repo).</param>
    /// <param name="skipPullStack">Don't pull the Stack repository itself.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Pull( bool all = false, bool skipPullStack = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.Pull( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, all, skipPullStack );
        } );
    }

    /// <summary>
    /// Fetches all branches of the current Repo or all the Repos of the current World. 
    /// </summary>
    /// <param name="all">Fetch from all the Repos of the current World (even if current path is in a Repo).</param>
    /// <param name="fromAllRemotes">Fetch from all available remotes, not only from 'origin'.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Fetch( bool all = false, bool fromAllRemotes = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.Fetch( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, all, fromAllRemotes );
        } );
    }

    /// <summary>
    /// Pushes the current Repo or all the current World's Repos current branches to their remotes.
    /// </summary>
    /// <param name="all">Push all the Repos of the current World (even if current path is in a Repo).</param>
    /// <param name="stackOnly">Only push the Stack repository, not the current Repo nor the Repos of the current World.</param>
    /// <param name="continueOnError">Push all the Repos even on error. By default the first error stops the push.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Push( bool all = false, bool stackOnly = false, bool continueOnError = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.Push( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, all, stackOnly, continueOnError );
        } );
    }
}
