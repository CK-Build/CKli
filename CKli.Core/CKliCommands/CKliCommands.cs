using CK.Core;
using CKli.Core;
using CSemVer;
using System;

namespace CKli;

/// <summary>
/// Shell independent command implementations. All boolean options must be false by default.
/// </summary>
public static class CKliCommands
{
    /// <summary>
    /// Clones a Stack and all its current world repositories in the current directory.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="stackUrl">The url stack repository to clone from. The repository name must end with '-Stack'.</param>
    /// <param name="private">Indicates a private repository. A Personal Access Token (or any other secret) is required.</param>
    /// <param name="allowDuplicate">Allows a stack that already exists locally to be cloned.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int Clone( IActivityMonitor monitor,
                             ISecretsStore secretsStore,
                             NormalizedPath path,
                             Uri stackUrl,
                             bool @private = false,
                             bool allowDuplicate = false )
    {
        using( var stack = StackRepository.Clone( monitor, secretsStore, stackUrl, !@private, path, allowDuplicate ) )
        {
            return stack != null
                    ? 0
                    : -1;
        }
    }

    /// <summary>
    /// Resynchronizes the current Repo or all the Repos of the current World from the remotes. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="all">True to pull all the repositories of the world even if <paramref name="path"/> is in the working folder of a repository.</param>
    /// <param name="skipPullStack">Don't pull the stack repository itself.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int Pull( IActivityMonitor monitor,
                            ISecretsStore secretsStore,
                            NormalizedPath path,
                            bool all = false,
                            bool skipPullStack = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                secretsStore,
                                                path,
                                                out var stack,
                                                out var world,
                                                skipPullStack ) )
        {
            return -1;
        }
        try
        {
            if( !all )
            {
                var repo = world.TryGetRepo( monitor, path );
                if( repo != null )
                {
                    return repo.Pull( monitor ).IsSuccess() ? 0 : -2;
                }
            }
            return world.Pull( monitor ) ? 0 : -2;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Fetches all branches of the current Repo or all the Repos of the current World. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="all">True to fetch all the repositories of the world even if <paramref name="path"/> is in the working folder of a repository.</param>
    /// <param name="fromAllRemotes">True to fetch from all available remotes, not only from 'origin'.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int Fetch( IActivityMonitor monitor,
                             ISecretsStore secretsStore,
                             NormalizedPath path,
                             bool all = false,
                             bool fromAllRemotes = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                secretsStore,
                                                path,
                                                out var stack,
                                                out var world,
                                                skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            if( !all )
            {
                var repo = world.TryGetRepo( monitor, path );
                if( repo != null )
                {
                    return repo.Fetch( monitor, originOnly: !fromAllRemotes ) ? 0 : -2;
                }
            }
            return world.Fetch( monitor, originOnly: !fromAllRemotes ) ? 0 : -2;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Pushes the current Repo or all the current World's Repos current branches to their remotes.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="all">True to pull all the repositories of the world even if <paramref name="path"/> is in the working folder of a repository.</param>
    /// <param name="stackOnly">Only push the stack repository, not the repositories of the current world.</param>
    /// <param name="continueOnError">Push all the repositories even on error. By default the first error stops the pushes.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int Push( IActivityMonitor monitor,
                            ISecretsStore secretsStore,
                            string path,
                            bool all = false,
                            bool stackOnly = false,
                            bool continueOnError = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor,
                                                secretsStore,
                                                path,
                                                out var stack,
                                                out var world,
                                                skipPullStack: false ) )
        {
            return -1;
        }
        try
        {
            if( !all )
            {
                var repo = world.TryGetRepo( monitor, path );
                if( repo != null )
                {
                    return repo.Push( monitor ) ? 0 : -2;
                }
            }
            if( !stack.PushChanges( monitor ) )
            {
                return -2;
            }
            return stackOnly
                    ? 0
                    : world.Push( monitor, !continueOnError )
                        ? 0
                        : -2;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Adds a new repository to the current world. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="repositoryUrl">Url of the repository to add and clone.</param>
    /// <param name="allowLTS">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int RepositoryAdd( IActivityMonitor monitor,
                                     ISecretsStore secretsStore,
                                     NormalizedPath path,
                                     Uri repositoryUrl,
                                     bool allowLTS = false )
    {
        if( !StackRepository.OpenFromPath( monitor, secretsStore, path, out var stack, skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            var worldName = stack.GetWorldNameFromPath( monitor, path );
            if( worldName == null )
            {
                return -2;
            }
            if( !allowLTS && !worldName.IsDefaultWorld )
            {
                return RequiresAllowLTS( monitor, worldName );
            }
            return worldName.AddRepository( monitor, repositoryUrl, path )
                    ? 0
                    : -4;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Removes a repository from the current world. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="nameOrUrl">Name or url of the repository to remove.</param>
    /// <param name="allowLTS">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int RepositoryRemove( IActivityMonitor monitor,
                                        ISecretsStore secretsStore,
                                        NormalizedPath path,
                                        string nameOrUrl,
                                        bool allowLTS = false )
    {
        if( !StackRepository.OpenFromPath( monitor, secretsStore, path, out var stack, skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            var worldName = stack.GetWorldNameFromPath( monitor, path );
            if( worldName == null )
            {
                return -2;
            }
            if( !allowLTS && !worldName.IsDefaultWorld )
            {
                return RequiresAllowLTS( monitor, worldName );
            }
            return worldName.RemoveRepository( monitor, nameOrUrl )
                    ? 0
                    : -3;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Fixes the the folders and repositories layout of the current world. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="deleteAliens">Delete repositories that don't belong to the current world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int LayoutFix( IActivityMonitor monitor,
                                 ISecretsStore secretsStore,
                                 NormalizedPath path,
                                 bool deleteAliens = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, secretsStore, path, out var stack, out var world, skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            return world.FixLayout( monitor, deleteAliens, out _ )
                    ? 0
                    : -2;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Updates the layout of the current world from existing folders and repositories.
    /// To share this updated layout with others, 'push --stackOnly' must be executed. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int LayoutXif( IActivityMonitor monitor,
                                 ISecretsStore secretsStore,
                                 NormalizedPath path )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, secretsStore, path, out var stack, out var world, skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            return world.XifLayout( monitor )
                    ? 0
                    : -2;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Creates a new source based plugin project for the current World.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="pluginName">The new plugin to create.</param>
    /// <param name="allowLTS">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int PluginCreate( IActivityMonitor monitor,
                                    ISecretsStore secretsStore,
                                    string path,
                                    string pluginName,
                                    bool allowLTS = false )
    {
        return CreateOrRemovePlugin( monitor, secretsStore, path, pluginName, allowLTS, create: true );
    }

    /// <summary>
    /// Fully removes a plugin from the current World. It must not have dependent plugins otherwise this fails.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="pluginName">The plugin to remove.</param>
    /// <param name="allowLTS">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int PluginRemove( IActivityMonitor monitor,
                                    ISecretsStore secretsStore,
                                    string path,
                                    string pluginName,
                                    bool allowLTS = false )
    {
        return CreateOrRemovePlugin( monitor, secretsStore, path, pluginName, allowLTS, create: false );
    }

    static int CreateOrRemovePlugin( IActivityMonitor monitor, ISecretsStore secretsStore, string path, string pluginName, bool allowLTS, bool create )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, secretsStore, path, out var stack, out var world, skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            if( world.DefinitionFile.IsPluginsDisabled )
            {
                return RequiresEnabledPlugins( monitor );
            }
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return RequiresAllowLTS( monitor, world.Name );
            }
            return (create ? world.CreatePlugin( monitor, pluginName ) : world.RemovePlugin( monitor, pluginName ))
                    ? 0
                    : -4;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Dumps information about installed plugins.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int PluginInfo( IActivityMonitor monitor,
                                  ISecretsStore secretsStore,
                                  string path )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, secretsStore, path, out var stack, out var world, skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            bool success = world.RaisePluginInfo( monitor, out var text );
            Console.WriteLine( text );
            return success ? 0 : -2;
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Adds a new plugin (or sets the version of an existing one) in the current world's plugins. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="packageId">"Name@Version" or ""CKli.Name.Plugin@Version" to add. Actual CKli plugin packages are "CKli.XXX.Plugin".</param>
    /// <param name="version">Package's version.</param>
    /// <param name="allowLTS">Allows the current world to be a Long Term Support world.</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int PluginAdd( IActivityMonitor monitor,
                                 ISecretsStore secretsStore,
                                 string path,
                                 string packageId,
                                 SVersion version,
                                 bool allowLTS = false )
    {
        if( !StackRepository.OpenWorldFromPath( monitor, secretsStore, path, out var stack, out var world, skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            if( world.DefinitionFile.IsPluginsDisabled )
            {
                return RequiresEnabledPlugins( monitor );
            }
            if( !allowLTS && !world.Name.IsDefaultWorld )
            {
                return RequiresAllowLTS( monitor, world.Name );
            }
            return world.AddOrSetPluginPackage( monitor, packageId, version )
                    ? 0
                    : -4;
        }
        finally
        {
            stack.Dispose();
        }
    }

    static int RequiresEnabledPlugins( IActivityMonitor monitor )
    {
        monitor.Error( $"Plugins are disabled for this world." );
        return -2;
    }

    static int RequiresAllowLTS( IActivityMonitor monitor, LocalWorldName worldName )
    {
        monitor.Error( $"""
                        Current world '{worldName}' is not the default world.
                        --allow-LTS option must be specified.
                        """ );
        return -3;
    }

    /// <summary>
    /// Disables a plugin or all of them if <paramref name="name"/> is "global".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="name">The plugin to disable or "global".</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int PluginDisable( IActivityMonitor monitor,
                                     ISecretsStore secretsStore,
                                     string path,
                                     string name )
    {
        return EnableOrDisablePlugin( monitor, secretsStore, path, name, enable: false );
    }

    /// <summary>
    /// Enables a plugin or all of them if <paramref name="name"/> is "global".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store.</param>
    /// <param name="path">The current path to consider.</param>
    /// <param name="name">The plugin to enable or "global".</param>
    /// <returns>0 on success, negative on error.</returns>
    public static int PluginEnable( IActivityMonitor monitor,
                                    ISecretsStore secretsStore,
                                    string path,
                                    string name )
    {
        return EnableOrDisablePlugin( monitor, secretsStore, path, name, enable: true );
    }

    static int EnableOrDisablePlugin( IActivityMonitor monitor, ISecretsStore secretsStore, string path, string name, bool enable )
    {
        if( !StackRepository.OpenFromPath( monitor, secretsStore, path, out var stack, skipPullStack: true ) )
        {
            return -1;
        }
        try
        {
            var definitionFile = stack.GetWorldNameFromPath( monitor, path )?.LoadDefinitionFile( monitor );
            if( definitionFile == null ) return -2;
            
            return definitionFile.EnablePlugin( monitor, name, enable )
                    ? 0
                    : -4;
        }
        finally
        {
            stack.Dispose();
        }
    }
}
