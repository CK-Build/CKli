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
    public class XWorld : XTypedObject, IXTypedObjectProvider<World>
    {
        readonly FileSystem _fileSystem;
        readonly IEnvLocalFeedProvider _localFeeds;
        readonly World _world;
        readonly IActivityMonitorFilteredClient _userMonitorFilter;

        public XWorld(
            FileSystem fileSystem,
            IRootedWorldName worldName,
            WorldStore worldStore,
            IEnvLocalFeedProvider localFeeds,
            SecretKeyStore keyStore,
            ArtifactCenter artifacts,
            CommandRegister commandRegister,
            IBasicApplicationLifetime appLife,
            Initializer initializer )
            : base( initializer )
        {
            _localFeeds = localFeeds;
            _fileSystem = fileSystem;

            _userMonitorFilter = initializer.Monitor.Output.Clients.OfType<IActivityMonitorFilteredClient>().FirstOrDefault();
            if( _userMonitorFilter == null ) throw new InvalidOperationException();

            bool isPublic = initializer.Reader.HandleRequiredAttribute<bool>( "IsPublic" );
            _world = new World( commandRegister, artifacts, worldStore, worldName, isPublic, _localFeeds, keyStore, _userMonitorFilter, appLife )
            {
                VersionSelector = new ReleaseVersionSelector()
            };
            _world.DumpWorldStatus += ( o, e ) => OnDumpWorldStatus( e.Monitor );
            initializer.Services.Add( _world );
            fileSystem.ServiceContainer.Add<ISolutionDriverWorld>( _world );
        }

        /// <summary>
        /// Initializes the world state and publishes the <see cref="World.LocalWorldState"/>
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
        /// Initializes the Git folders: this instanciates the <see cref="GitFolder"/> from the
        /// <see cref="XGitFolder.ProtoGitFolder"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The world  on success, null on error.</returns>
        World IXTypedObjectProvider<World>.GetObject( IActivityMonitor m )
        {
            var proto = Parent.Descendants<XGitFolder>().Select( g => g.ProtoGitFolder ).ToList();
            List<GitFolder> gitFolders = new List<GitFolder>();

            using( m.OpenInfo( $"Initializing {proto.Count} Git folders." ) )
            {
                try
                {
                    foreach( var p in proto )
                    {
                        var g = _fileSystem.EnsureGitFolder( m, p );
                        if( g != null ) gitFolders.Add( g );
                        else
                        {
                            m.Error( $"GitFolder creation failed for {p.FolderPath}." );
                            return null;
                        }
                    }
                    DumpGitFolders( m, gitFolders );
                    return _world;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return null;
                }
            }
        }

        void OnDumpWorldStatus( IActivityMonitor m )
        {
            var worldCtx = _world.SolutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
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
            var msg = $"Monitor filters: User:'{_userMonitorFilter.MinimalFilter}' => Final:'{final}'{(isLogFilterDefault ? "(AppDomain's default)" : "")}.";
            m.UnfilteredLog( ActivityMonitor.Tags.Empty, LogLevel.Info, msg, m.NextLogTime(), null );

            int gitFoldersCount = 0;
            bool hasPluginInitError = false;
            var dirty = new List<string>();
            foreach( var git in gitFolders )
            {
                ++gitFoldersCount;
                string commitAhead = git.Head.AheadOriginCommitCount != null ? $"{git.Head.AheadOriginCommitCount} commits ahead origin" : "Untracked";
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
