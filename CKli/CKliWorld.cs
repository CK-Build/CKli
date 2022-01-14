using CK.Core;
using CK.Build;
using CK.Env.DependencyModel;
using CK.SimpleKeyVault;

using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using CK.Env;

namespace CKli
{
    /// <summary>
    /// World object for CKli console.
    /// Supports specific command line commands.
    /// </summary>
    public class CKliWorld : World
    {
        readonly IActivityMonitorFilteredClient _userMonitorClient;

        /// <summary>
        /// Initializes a new World.
        /// </summary>
        /// <param name="parameters">Available parameters.</param>
        public CKliWorld( ConstructorParameters parameters )
            : base( parameters )
        {
            _userMonitorClient = parameters.InitializationMonitor.Output.Clients.OfType<IActivityMonitorFilteredClient>().FirstOrDefault();
            if( _userMonitorClient == null ) throw new InvalidOperationException();
        }

        protected override void OnInitialize( IActivityMonitor monitor )
        {
            DoSetLogLevel( monitor, LocalWorldState.UserLogFilter, LocalWorldState.MonitorLogFilter, false );
        }

        #region Dumping information to the monitor and/or the console. These are command line specifics.

        /// <summary>
        /// Asks this world state to be dumped (in the monitor/console) by
        /// raising <see cref="DumpWorldStatus"/> event.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod( confirmationRequired: false )]
        public bool DumpWorldState( IActivityMonitor monitor )
        {
            return RunSafe( monitor, "World Status.", ( m, error ) =>
            {
                var ev = new EventMonitoredArgs( monitor );
                DumpWorldStatus?.Invoke( this, ev );
            } );
        }

        /// <summary>
        /// Raised by <see cref="DumpWorldState(IActivityMonitor)"/>.
        /// </summary>
        public event EventHandler<EventMonitoredArgs>? DumpWorldStatus;

        [CommandMethod]
        public void ShowExternalDependencies( IActivityMonitor m, bool compact = true, bool onlyMultipleVersions = false, PackageQuality quality = PackageQuality.None )
        {
            var ctx = SolutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
            if( ctx == null ) return;
            var externals = GetExternalPackageReferences( m, ctx );
            ConsoleColor stdForeColor = Console.ForegroundColor;
            ConsoleColor stdBackColor = Console.BackgroundColor;
            foreach( var byType in externals.GroupBy( g => g.Target.Artifact.Type ).OrderBy( g => g.Key.Name ) )
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.BackgroundColor = ConsoleColor.White;
                Console.WriteLine( $"{byType.Key} external dependencies:" );
                Console.ForegroundColor = stdForeColor;
                Console.BackgroundColor = stdBackColor;
                foreach( var byName in byType.GroupBy( g => g.Target.Artifact ).OrderBy( g => g.Key.Name ) )
                {
                    var byVersion = byName.GroupBy( s => s.Target.Version ).ToList();
                    if( !onlyMultipleVersions || byVersion.Count() > 1 )
                    {
                        var maxVersion = byVersion.Select( v => v.Key ).Max();
                        var externalVersionDisplay = Artifacts.GetExternalVersions( m, byName.Key )
                                                              .SelectMany( a => a.Versions.Where( v => v.PackageQuality >= quality && v > maxVersion ).Select( v => (v, a.FeedName) ) )
                                                              .GroupBy( v => v.v )
                                                              .OrderByDescending( v => v.Key )
                                                              .Select( g => $"{g.Key} ({g.Select( vn => vn.FeedName ).Concatenate()})" )
                                                              .Concatenate();

                        if( externalVersionDisplay.Length > 0 ) externalVersionDisplay = " <= " + externalVersionDisplay;
                        Console.Write( $"    |" );
                        Console.Write( byName.Key.Name );
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine( externalVersionDisplay );
                        Console.ForegroundColor = stdForeColor;
                        if( byVersion.Count() > 1 ) Console.ForegroundColor = ConsoleColor.DarkYellow;
                        if( compact )
                        {
                            foreach( var v in byVersion )
                            {
                                Console.WriteLine( $"    |      => {v.Key} ({v.GroupBy( p => p.Referer.Solution ).Select( s => $"{s.Key.Name}" ).Concatenate()})" );
                            }
                        }
                        else
                        {
                            foreach( var versionGrouped in byVersion )
                            {
                                Console.WriteLine( "    |    |" + versionGrouped.Key );
                                foreach( var solutionGrouped in versionGrouped.GroupBy( q => q.Referer.Solution ) )
                                {
                                    Console.WriteLine( "    |    |    |" + solutionGrouped.Key.Name + ":" );
                                    foreach( var project in solutionGrouped )
                                    {
                                        Console.WriteLine( "    |    |    |    |" + project.Referer.Name );
                                    }
                                }
                            }
                        }
                        Console.ForegroundColor = stdForeColor;
                    }
                }
            }
        }

        #endregion

        #region LogLevel change: this is only for command line interface.
        [CommandMethod( confirmationRequired: false )]
        public void SetLogLevel( IActivityMonitor m, LogFilter userLevel, LogFilter monitorLevel ) => DoSetLogLevel( m, userLevel, monitorLevel, true );

        void DoSetLogLevel( IActivityMonitor m, LogFilter userLevel, LogFilter monitorLevel, bool saveOnChange )
        {
            if( _userMonitorClient.MinimalFilter != userLevel )
            {
                _userMonitorClient.MinimalFilter = userLevel;
                LocalWorldState.UserLogFilter = userLevel;
            }
            if( m.MinimalFilter != monitorLevel )
            {
                m.MinimalFilter = monitorLevel;
                LocalWorldState.MonitorLogFilter = monitorLevel;
            }
            var msg = $"Log levels: UserLevel = '{userLevel}', MonitorLevel = {monitorLevel}.";
            Console.WriteLine( msg );
            m.UnfilteredLog( LogLevel.Info, ActivityMonitor.Tags.Empty,  msg, null );
            if( saveOnChange ) LocalWorldState.SaveState( m );
        }
        #endregion

        #region EditRoadMap
        /// <summary>
        /// Same as <see cref="CanRelease"/>.
        /// </summary>
        public bool CanEditRoadmap => CanRelease;

        /// <summary>
        /// Edits the current roadmap or creates one.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The roadmap.</returns>
        [CommandMethod]
        public bool EditRoadmap( IActivityMonitor monitor, bool pull = true )
        {
            Throw.CheckState( CanEditRoadmap );
            if( !CheckBeforeReleaseBuildOrEdit( monitor, pull ) ) return false;
            return DoEditRoadmap( monitor, false ) != null;
        }

        ReleaseRoadmap? DoEditRoadmap( IActivityMonitor monitor, bool forgetAllExistingRoadmapVersions )
        {
            Debug.Assert( VersionSelector != null );
            var roadmap = LoadRoadmap( monitor );
            if( roadmap == null ) return null;
            bool editSucceed = roadmap.UpdateRoadmap( monitor, VersionSelector, forgetAllExistingRoadmapVersions );
            // Always saves state to preserve any change in the roadmap, even on error.
            LocalWorldState.Roadmap = roadmap.ToXml();
            if( !LocalWorldState.SaveState( monitor ) || !editSucceed ) return null;
            return roadmap;
        }

        #endregion

        /// <summary>
        /// Starts a release after an optional pull, using the current <see cref="VersionSelector"/>.
        /// This also checks/updates the roadmap: this is why this implementation is in the CKliWorld and not in
        /// the base World.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="pull">Pull all branches first.</param>
        /// <param name="resetRoadmap">True to forget the current road-map (it if exists) and ask for each and every version.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Release( IActivityMonitor monitor, bool pull = true, bool resetRoadmap = false )
        {
            Throw.CheckState( CanRelease );
            if( !CheckBeforeReleaseBuildOrEdit( monitor, pull ) ) return false;

            var roadmap = DoEditRoadmap( monitor, resetRoadmap );
            if( roadmap == null ) return false;

            if( !SetWorkStatusAndSave( monitor, GlobalWorkStatus.Releasing ) ) return false;

            return DoReleasing( monitor );
        }

        [CommandMethod]
        public void DumpWorldGraph( IActivityMonitor m, bool withProjects )
        {
            var s = withProjects ? CreatWorldGraphWithProjects( m ) : CreateWorldGraph( m );
            if( s != null )
            {
                var path = Path.GetFullPath( "graph.gv" );
                File.WriteAllText( path, s );
                m.Info( $"Generated graph: {path}" );
            }
        }



    }

}
