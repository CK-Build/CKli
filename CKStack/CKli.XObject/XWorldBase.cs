using CK.Core;
using CK.Env;
using CK.SimpleKeyVault;

using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace CKli
{
    /// <summary>
    /// Required element that defines the whole world.
    /// A world is public (ie. Open Source) or not. We decided to consider that mixing public/private repositories 
    /// inside the same world (even if it is technically possible) was not a good idea.
    /// </summary>
    public abstract class XWorldBase<T> : XTypedObject, IXTypedObjectProvider<T> where T : World
    {
        readonly FileSystem _fileSystem;
        readonly IEnvLocalFeedProvider _localFeeds;
        readonly T _world;

        public XWorldBase( FileSystem fileSystem,
                           IRootedWorldName worldName,
                           IWorldStore worldStore,
                           IEnvLocalFeedProvider localFeeds,
                           SecretKeyStore keyStore,
                           ArtifactCenter artifacts,
                           CommandRegistry commandRegister,
                           IReleaseVersionSelector releaseVersionSelector,
                           Initializer initializer )
            : base( initializer )
        {
            _localFeeds = localFeeds;
            _fileSystem = fileSystem;
            bool isPublic = initializer.Reader.HandleRequiredAttribute<bool>( "IsPublic" );
            var parameters = new World.ConstructorParameters( initializer.Monitor,
                                                              initializer.Services,
                                                              fileSystem,
                                                              commandRegister,
                                                              artifacts,
                                                              worldStore,
                                                              worldName,
                                                              isPublic,
                                                              localFeeds,
                                                              keyStore );
            _world = (T)Activator.CreateInstance( typeof(T), parameters )!;
            _world.VersionSelector = releaseVersionSelector;
            initializer.Services.Add( (World)_world );
            fileSystem.ServiceContainer.Add<ISolutionDriverWorld>( _world );
        }

        /// <summary>
        /// Initializes the world state with all the <see cref="XGitFolder"/> that appear after it
        /// and publishes the <see cref="World.LocalWorldState"/>
        /// and <see cref="World.SharedWorldState"/> in the <see cref="FileSystem.ServiceContainer"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        protected override bool OnSiblingsCreated( IActivityMonitor monitor )
        {
            if( !_world.Initialize( monitor ) ) return false;
            _fileSystem.ServiceContainer.Add( _world.SharedWorldState );
            _fileSystem.ServiceContainer.Add( _world.LocalWorldState );
            return true;
        }

        /// <summary>
        /// Gets the world (CKli is definitely an ambitious project).
        /// </summary>
        protected T World => _world;

        T? IXTypedObjectProvider<T>.GetObject( IActivityMonitor m ) => _world;

        /// <summary>
        /// Dumps the hierarchy of repositories into the monitor, ensuring that all
        /// plugins are loaded for the current branch.
        /// This is used at initialization time but can also be used
        /// by command line interface.
        /// </summary>
        /// <param name="m">The target monitor.</param>
        /// <param name="gitFolders">The set of git repositories.</param>
        /// <returns>False if at least one plugin failed its initialization. True otherwise.</returns>
        protected virtual bool DumpGitFolders( IActivityMonitor m, IEnumerable<GitRepository> gitFolders )
        {
            int gitFoldersCount = 0;
            bool hasPluginInitError = false;
            var dirty = new List<string>();
            foreach( var git in gitFolders )
            {
                ++gitFoldersCount;
                string commitAhead = git.Head.AheadOriginCommitCount != null ? $"{git.Head.AheadOriginCommitCount} commits ahead origin" : "Untracked";
                using( m.OpenInfo( $"{git.DisplayPath} - branch: {git.CurrentBranchName} ({commitAhead})." ) )
                {
                    string pluginInfo;
                    if( !git.EnsureCurrentBranchPlugins( m ) )
                    {
                        hasPluginInitError = true;
                        pluginInfo = "Plugin initialization error.";
                    }
                    else
                    {
                        pluginInfo = $"({git.PluginManager.BranchPlugins[git.CurrentBranchName].Count} plugins)";
                    }

                    if( git.CheckCleanCommit( m ) )
                    {
                        m.CloseGroup( "Up-to-date. " + pluginInfo );
                    }
                    else
                    {
                        dirty.Add( git.DisplayPath );
                        m.CloseGroup( "Dirty. " + pluginInfo );
                    }
                }
            }
            if( gitFoldersCount == 0 )
            {
                m.Error( "No git folder found." );
            }
            else
            {
                m.CloseGroup( $"{dirty.Count} dirty (out of {gitFoldersCount})." );
                if( dirty.Count > 0 ) m.Info( $"Dirty: {dirty.Concatenate()}" );
                var byActiveBranch = gitFolders.GroupBy( g => g.CurrentBranchName );
                if( byActiveBranch.Count() > 1 )
                {
                    using( m.OpenWarn( $"{byActiveBranch.Count()} different branches:" ) )
                    {
                        foreach( var b in byActiveBranch )
                        {
                            m.Warn( $"Branch '{b.Key}': " + b.Select( g => g.DisplayPath.Path ).Concatenate() );
                        }
                    }
                }
                else
                {
                    m.Info( $"All {gitFoldersCount} git folders are on '{byActiveBranch.First().Key}' branch." );
                }

                if( hasPluginInitError )
                {
                    m.Error( "At least one git folder is unable to initialize its plugins." );
                }
            }
            return !hasPluginInitError;
        }
    }
}
