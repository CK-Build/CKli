using CK.Core;
using CKli.Core;
using ConsoleAppFramework;
using System.IO;
using System;

namespace CKli;

class RootCommands
{
    readonly ISecretsStore _secretsStore;

    public RootCommands()
    {
        _secretsStore = new DotNetUserSecretsStore();
    }

    /// <summary>
    /// Clones a Stack and all its current world repositories to the local file system.
    /// </summary>
    /// <param name="repository">The stack repository to clone from. Its name ends with '-Stack'.</param>
    /// <param name="parentPath">-p, Parent folder of the cloned stack.</param>
    /// <param name="private">Indicates a private repository. A Personal Access Token (or any other secret) is required.</param>
    /// <param name="allowDuplicate">Allows a repository that already exists in "stack list" to be cloned.</param>
    public int Clone( [Argument, AbsoluteUrlParser] AbsoluteUrl repository,
                      string? parentPath = "<<Current Directory>>",
                      bool @private = false,
                      bool allowDuplicate = false )
    {
        return CommandContext.WithMonitor( monitor =>
        {
            var path = new NormalizedPath( Environment.CurrentDirectory )
                                .Combine( string.IsNullOrWhiteSpace( parentPath ) || parentPath[0] == '<' ? null : parentPath )
                                .ResolveDots( throwOnAboveRoot: false );

            return StackRepository.Clone( monitor, _secretsStore, repository.Url, !@private, path, allowDuplicate ) != null
                    ? -1
                    : 0;
        } );
    }
}
