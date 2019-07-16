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
        readonly IEnvLocalFeedProvider _localFeeds;
        readonly WorldState _worldState;

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
            _localFeeds = localFeeds;
            bool isPublic = initializer.Reader.HandleRequiredAttribute<bool>( "IsPublic" );
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
        /// Initializes this world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        protected override bool OnSiblingsCreated( IActivityMonitor monitor )
        {
            var folders = Parent.Descendants<XGitFolder>().Select( g => g.GitFolder );
            return _worldState.Initialize( monitor ) && DumpGitFolders( monitor, folders );
        }

        void OnDumpWorldStatus( IActivityMonitor m )
        {
             var worldCtx = _worldState.SolutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
            if( worldCtx == null ) return;
            var gitFolders = worldCtx.Drivers.Select( x => (GitFolder)x.GitRepository );
            DumpGitFolders( m, gitFolders );
        }

        bool DumpGitFolders( IActivityMonitor m, IEnumerable<GitFolder> gitFolders )
        {
            bool isLogFilterDefault = false;
            LogFilter final = m.ActualFilter;
            if( final == LogFilter.Undefined )
            {
                final = ActivityMonitor.DefaultFilter;
                isLogFilterDefault = true;
            }
            var msg = $"Monitor filters: Monitor.MinimalFilter:'{m.MinimalFilter}' => Final:'{final}'{(isLogFilterDefault ? "(AppDomain's default)" : "")}.";
            m.UnfilteredLog( ActivityMonitor.Tags.Empty, LogLevel.Info, msg, m.NextLogTime(), null );

            int gitFoldersCount = 0;
            bool hasPluginInitError = false;
            var dirty = new List<string>();
            foreach( var git in gitFolders )
            {
                ++gitFoldersCount;
                string commitAhead = git.AheadOriginCommitCount != null ? $"{git.AheadOriginCommitCount} commits ahead origin" : "Untracked";
                using( m.OpenInfo( $"{git.SubPath} - branch: {git.CurrentBranchName} ({commitAhead})." ) )
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
                            using( m.OpenWarn( $"Branch '{b.Key}':" ) )
                            {
                                m.Warn( b.Select( g => g.SubPath.Path ).Concatenate() );
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
