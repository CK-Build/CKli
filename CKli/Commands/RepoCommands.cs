using CK.Core;
using CKli.Core;
using ConsoleAppFramework;
using System;

namespace CKli;

[RegisterCommands( "repo" )]
public sealed class RepoCommands
{
    /// <summary>
    /// Adds a new repository to the current world. 
    /// </summary>
    /// <param name="repositoryUrl">Url of the repository to add and clone.</param>
    /// <param name="allowLTS">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Add( [Argument, AbsoluteUrlParser] AbsoluteUrl repositoryUrl, bool allowLTS = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.RepositoryAdd( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, repositoryUrl.Url, allowLTS );
        } );
    }

    /// <summary>
    /// Removes a repository from the current world. 
    /// </summary>
    /// <param name="nameOrUrl">Name or url of the repository to remove.</param>
    /// <param name="allowLTS">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public int Remove( string nameOrUrl, bool allowLTS = false )
    {
        return CommandContext.Run( ( monitor, userPreferences ) =>
        {
            return CKliCommands.RepositoryRemove( monitor, userPreferences.SecretsStore, Environment.CurrentDirectory, nameOrUrl, allowLTS );
        } );
    }

}
