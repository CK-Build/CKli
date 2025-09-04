using CK.Core;
using CKli.Core;
using ConsoleAppFramework;
using System;
using System.Linq;

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
    /// The target folder (in <paramref name="parentPath"/>) must not already exist.
    /// </summary>
    /// <param name="repository">The stack repository to clone from. Its name ends with '-Stack'.</param>
    /// <param name="parentPath">-p, Parent folder of the cloned stack. Can be absolute or relative to the current directory.</param>
    /// <param name="private">Indicates a private repository. A Personal Access Token (or any other secret) is required.</param>
    /// <param name="allowDuplicate">Allows a repository that already exists in "stack list" to be cloned.</param>
    /// <returns>0 on success, -1 on error.</returns>
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

    /// <summary>
    /// Updates the stack. 
    /// </summary>
    /// <param name="path">-p, Path of the stack. By default, the stack folder above is found.</param>
    /// <param name="updateStack">False to not pull the stack repository itself.</param>
    /// <returns>0 on success, -1 on error.</returns>
    public int Pull( string? path = "<<From Current Directory>>",
                     bool updateStack = true )
    {
        return CommandContext.WithMonitor( monitor =>
        {
            var p = new NormalizedPath( Environment.CurrentDirectory )
                                .Combine( string.IsNullOrWhiteSpace( path ) || path[0] == '<' ? null : path )
                                .ResolveDots( throwOnAboveRoot: false );
            if( !StackRepository.TryOpenFrom( monitor, _secretsStore, p, out var stackRepository, skipPullMerge: !updateStack ) )
            {
                return -1;
            }
            if( stackRepository == null )
            {
                monitor.Error( $"Unable to find a stack repository from path '{p}'." );
                return -2;
            }
            Throw.DebugAssert( p.StartsWith( stackRepository.StackRoot, strict: false ) );
            var worldName = stackRepository.GetWorldFromPath( monitor, p );
            if( worldName == null )
            {
                return -3;
            }
            var layout = worldName.LoadDefinitionFile( monitor )?.ReadLayout( monitor );
            if( layout == null )
            {
                return -4;
            }
            using( monitor.OpenInfo( worldName.IsDefaultWorld
                                    ? $"Pulling {layout.Count} repositories of '{worldName.StackName}''s default world."
                                    : $"Pulling {layout.Count} repositories of LTS world '{worldName.LTSName}'." ) )
            {
            }
        } );
    }

}
