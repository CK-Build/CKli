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
    /// Resynchronizes the current world from the remotes. 
    /// </summary>
    /// <param name="path">-p, Path of the world. By default, the world that contains the current path is resolved.</param>
    /// <param name="pullStack">False to not pull the stack repository itself.</param>
    /// <returns>0 on success, -1 on error.</returns>
    public int Pull( string? path = "<<From Current Directory>>",
                     bool pullStack = true )
    {
        return CommandContext.WithMonitor( monitor =>
        {
            World? world = TryOpenWorldFromPath( monitor, path, pullStack, out var absolutePath, out int errorCode );
            if( world == null ) return errorCode;
            try
            {
                using( monitor.OpenInfo( world.Name.IsDefaultWorld
                                        ? $"Pulling {world.Layout.Count} repositories of '{world.Name.StackName}' default world."
                                        : $"Pulling {world.Layout.Count} repositories of LTS world '{world.Name.LTSName}'." ) )
                {
                    if( !world.FixLayout( monitor, deleteAliens: false, out var newClones ) )
                    {
                        return -4;
                    }
                    var all = world.EnsureAllGitRepositories( monitor );
                    if( all == null )
                    {
                        return -5;
                    }
                    bool success = true;
                    foreach( var g in all )
                    {
                        if( !newClones.Contains( g ) )
                        {
                            success &= g.Pull( monitor ).IsSuccess();
                        }
                    }
                    return success ? 0 : -6;
                }

            }
            finally
            {
                world?.StackRepository?.Dispose();
                world?.Dispose();
            }
        } );
    }

    StackRepository? TryOpenStackRepositoryFromPath( IActivityMonitor monitor,
                                                     string? inputPath,
                                                     bool pullStack,
                                                     out NormalizedPath path,
                                                     out int errorCode )
    {
        errorCode = 0;
        path = new NormalizedPath( Environment.CurrentDirectory )
                            .Combine( string.IsNullOrWhiteSpace( inputPath ) || inputPath[0] == '<' ? null : inputPath )
                            .ResolveDots( throwOnAboveRoot: false );
        if( !StackRepository.TryOpenFrom( monitor, _secretsStore, path, out var stackRepository, skipPullMerge: !pullStack ) )
        {
            errorCode = -1;
            return null;
        }
        if( stackRepository == null )
        {
            monitor.Error( $"Unable to find a stack repository from path '{path}'." );
            errorCode = -2;
            return null;
        }
        return stackRepository;
    }

    World? TryOpenWorldFromPath( IActivityMonitor monitor,
                                 string? inputPath,
                                 bool pullStack,
                                 out NormalizedPath path,
                                 out int errorCode )
    {
        StackRepository? stackRepository = TryOpenStackRepositoryFromPath( monitor, inputPath, pullStack, out path, out errorCode );
        if( stackRepository == null ) return null;
        Throw.DebugAssert( path.StartsWith( stackRepository.StackRoot, strict: false ) );
        var world = World.TryOpenFromPath( monitor, stackRepository, path );
        if( world == null )
        {
            errorCode = -3;
        }
        return world;
    }
}
