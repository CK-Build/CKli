using CK.Core;
using CK.Env;
using CK.SimpleKeyVault;

using SimpleGitVersion;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CKli
{
    /// <summary>
    /// CKli specific world.
    /// Handles the log level filter.
    /// </summary>
    [XName( "World" )]
    public class XCKliWorld : XWorldBase<CKliWorld>
    {
        readonly IActivityMonitorFilteredClient _userMonitorFilter;

        public XCKliWorld( FileSystem fileSystem,
                           IRootedWorldName worldName,
                           IWorldStore worldStore,
                           IEnvLocalFeedProvider localFeeds,
                           SecretKeyStore keyStore,
                           ArtifactCenter artifacts,
                           CommandRegistry commandRegister,
                           IReleaseVersionSelector releaseVersionSelector,
                           Initializer initializer )
        : base( fileSystem, worldName, worldStore, localFeeds, keyStore, artifacts, commandRegister, releaseVersionSelector, initializer )
        {
            _userMonitorFilter = initializer.Monitor.Output.Clients.OfType<IActivityMonitorFilteredClient>().FirstOrDefault();
            Debug.Assert( _userMonitorFilter != null, "Otherwise, the CKliWorld constructor would have thrown." );
            World.DumpWorldStatus += ( o, e ) => OnDumpWorldStatus( e.Monitor );
        }

        void OnDumpWorldStatus( IActivityMonitor m )
        {
            var worldCtx = World.SolutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
            if( worldCtx == null ) return;
            var gitFolders = worldCtx.Drivers.Select( x => x.GitRepository );
            DumpGitFolders( m, gitFolders );
        }

        protected override bool DumpGitFolders( IActivityMonitor m, IEnumerable<GitRepository> gitFolders )
        {
            bool isLogFilterDefault = false;
            LogFilter final = m.ActualFilter;
            if( final == LogFilter.Undefined )
            {
                final = ActivityMonitor.DefaultFilter;
                isLogFilterDefault = true;
            }
            var msg = $"Monitor filters: User:'{_userMonitorFilter.MinimalFilter}' => Final:'{final}'{(isLogFilterDefault ? "(AppDomain's default)" : "")}.";
            m.UnfilteredLog( LogLevel.Info, ActivityMonitor.Tags.Empty, msg, null );
            return base.DumpGitFolders( m, gitFolders );
        }
    }

}
