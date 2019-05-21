using CK.Core;
using CK.Env;
using CK.Text;
using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli
{
    /// <summary>
    /// Required element that defines the whole world.
    /// A world is public (ie. Open Source) or not. We decided to consider that mixing public/private repositories 
    /// inside the same world (even if it is technically possible) was not a good idea.
    /// </summary>
    public class XWorldState : XTypedObject
    {
        readonly IWorldName _world;
        readonly IWorldStore _worldStore;
        readonly IEnvLocalFeedProvider _localFeeds;
        readonly WorldState _worldState;
        readonly CommandRegister _commandRegister;

        public XWorldState(
            FileSystem fileSystem,
            IWorldName world,
            IWorldStore worldStore,
            IEnvLocalFeedProvider localFeeds,
            ArtifactCenter artifacts,
            CommandRegister commandRegister,
            IBasicApplicationLifetime appLife,
            Initializer initializer )
            : base( initializer )
        {
            _world = world;
            _worldStore = worldStore;
            _localFeeds = localFeeds;
            _commandRegister = commandRegister;
            bool isPublic = (bool)initializer.HandleRequiredAttribute( "IsPublic" );
            _worldState = new WorldState( commandRegister, artifacts, worldStore, world, isPublic, _localFeeds, appLife )
            {
                VersionSelector = new ReleaseVersionSelector()
            };
            _worldState.DumpWorldStatus += ( o, e ) => OnDumpWorldStatus( e.Monitor );
            initializer.Services.Add( _worldState );
            fileSystem.ServiceContainer.Add<ISolutionDriverWorld>( _worldState );
        }

        /// <summary>
        /// Gets the current world.
        /// </summary>
        public IWorldName World => _world;


        /// <summary>
        /// Initializes this world..
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        protected override bool OnSiblingsCreated( IActivityMonitor monitor )
        {
            var folders = Parent.Descendants<XGitFolder>().Select( g => g.GitFolder );
            return DumpGitFolders( monitor, folders ) && _worldState.Initialize( monitor );
        }

        void OnDumpWorldStatus( IActivityMonitor m )
        {
            var gitFolders = _worldState.SolutionDrivers.Select( x => (GitFolder)x.GitRepository );
            DumpGitFolders( m, gitFolders );
        }

        static bool DumpGitFolders( IActivityMonitor m, IEnumerable<GitFolder> gitFolders )
        {
            int gitFoldersCount = 0;
            bool hasPluginInitError = false;
            var dirty = new List<string>();
            foreach( var git in gitFolders )
            {
                ++gitFoldersCount;
                using( m.OpenInfo( $"{git.SubPath} - branch: {git.CurrentBranchName}." ) )
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
                        dirty.Add( git.SubPath );
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
                            using( m.OpenInfo( $"Branch '{b.Key}':" ) )
                            {
                                m.Info( b.Select( g => g.SubPath.Path ).Concatenate() );
                            }
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
