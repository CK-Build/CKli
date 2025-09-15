using CK.Core;
using CKli.Core;
using ConsoleAppFramework;
using System;

namespace CKli;

[RegisterCommands]
public sealed class RootCommands
{
    /// <summary>
    /// Clones a Stack and all its current world repositories in the current directory.
    /// </summary>
    /// <param name="stackUrl">The url stack repository to clone from. The repository name must end with '-Stack'.</param>
    /// <param name="private">Indicates a private repository. A Personal Access Token (or any other secret) is required.</param>
    /// <param name="allowDuplicate">Allows a stack that already exists locally to be cloned.</param>
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
    /// Resynchronizes the current world from the remotes. 
    /// </summary>
    /// <param name="skipPullStack">Don't pull the stack repository itself.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Pull( bool skipPullStack = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.Pull( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, skipPullStack );
        } );
    }

    /// <summary>
    /// Pushes the repositories' current branches of the current world to the remotes.
    ///              A 'pull' is done first to detect any potential conflicts with the remotes' state. 
    /// </summary>
    /// <param name="stackOnly">Only push the stack repository, not the repositories of the current world.</param>
    /// <param name="continueOnError">Push all the repositories even on error. By default the first error stops the pushes.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Push( bool stackOnly = false, bool continueOnError = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.Push( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, stackOnly, continueOnError );
        } );
    }
}
