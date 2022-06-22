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
        /// <summary>
        /// Initializes a new World.
        /// </summary>
        /// <param name="parameters">Available parameters.</param>
        public CKliWorld( ConstructorParameters parameters )
            : base( parameters )
        {
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
        public void ShowExternalDependencies( IActivityMonitor m,
                                              bool refreshExternalVersions = false,
                                              bool onlyMultipleVersions = false,
                                              PackageQuality quality = PackageQuality.None )
        {
            var ctx = SolutionDrivers.GetSolutionDependencyContextOnCurrentBranches( m );
            if( ctx == null ) return;
            var externals = GetExternalPackageReferences( m, ctx, refreshExternalVersions );
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
                        var externalVersionDisplay = Artifacts.GetExternalVersions( m, byName.Key, false )
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
                        foreach( var v in byVersion )
                        {
                            Console.WriteLine( $"    |      => {v.Key} ({v.GroupBy( p => p.Referer.Solution ).Select( s => $"{s.Key.Name}" ).Concatenate()})" );
                        }
                        Console.ForegroundColor = stdForeColor;
                    }
                }
            }
        }

        [CommandMethod]
        public void UpgradeExternalDependencies( IActivityMonitor monitor,
                                                 bool refreshExternalVersions = false,
                                                 PackageQuality quality = PackageQuality.None )
        {
            var ctx = SolutionDrivers.GetSolutionDependencyContextOnCurrentBranches( monitor );
            if( ctx == null ) return;
            // Only NuGet is supported.
            var externals = GetExternalPackageReferences( monitor, ctx, refreshExternalVersions ).Where( p => p.Target.Artifact.Type?.Name == "NuGet" );
            ConsoleColor stdForeColor = Console.ForegroundColor;
            ConsoleColor stdBackColor = Console.BackgroundColor;
            foreach( var byName in externals.GroupBy( g => g.Target.Artifact ).OrderBy( g => g.Key.Name ) )
            {
                var byVersion = byName.GroupBy( s => s.Target.Version ).ToList();
                var maxVersion = byVersion.Select( v => v.Key ).Max();
                Debug.Assert( maxVersion != null );
                // We consider greater versions or lower versions with a better quality than the current max version used.
                var versionAndFeedNames = Artifacts.GetExternalVersions( monitor, byName.Key, false )
                                                    .SelectMany( a => a.Versions.Where( v => v.PackageQuality >= quality && (v > maxVersion || v.PackageQuality > maxVersion.PackageQuality) )
                                                        .Select( v => (v, a.FeedName) ) )
                                                    .GroupBy( v => v.v )
                                                    .OrderByDescending( v => v.Key )
                                                    .Select( g => ( Version: g.Key, FeedNames: g.Select( vn => vn.FeedName ).ToArray() ) )
                                                    .ToList();

                var externalVersionDisplay = versionAndFeedNames.Select( g => $"{g.Version} ({g.FeedNames.Concatenate()})" ).Concatenate();

                Console.Write( $" - " );
                Console.Write( byName.Key.Name );
                if( externalVersionDisplay.Length > 0 )
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    externalVersionDisplay = " <= " + externalVersionDisplay;
                    Console.WriteLine( externalVersionDisplay );
                    Console.ForegroundColor = stdForeColor;
                }
                if( byVersion.Count() > 1 ) Console.ForegroundColor = ConsoleColor.DarkYellow;
                foreach( var v in byVersion )
                {
                    Console.WriteLine( $"          => {v.Key} ({v.GroupBy( p => p.Referer.Solution ).Select( s => $"{s.Key.Name}" ).Concatenate()})" );
                }
                Console.ForegroundColor = stdForeColor;

                // Ask to choose or pass.
                // Uses the versionAndFeedNames that is a list to capture the choices.
                if( byVersion.Count > 1 )
                {
                    // The solutions references more than one version.
                    // The maxVersion used is not in external available versions (the filter
                    // above restricts them to version strictly greater than maxVersion).
                    versionAndFeedNames.Insert( 0, (maxVersion, new[] { "*current greatest version used*" } ) );
                }
                if( versionAndFeedNames.Count == 0 )
                {
                    Console.WriteLine( "=> No available upgrades." );
                }
                else
                {
                    Console.Write( "Please choose a version: N - Skip, " );
                    Console.WriteLine( versionAndFeedNames.Select( ( x, i ) => $"{i+1} - {x.Version} ({x.FeedNames.Concatenate()})" ).Concatenate() );
                    int idx = -1;
                    for( ; ; )
                    {
                        char a = Console.ReadKey().KeyChar;
                        if( a == 'N' ) break;
                        int n = a - '0';
                        if( n < 1 || n > versionAndFeedNames.Count ) continue;
                        idx = n - 1;
                        break;
                    }
                    Console.Write( "\r\x1b[K\r\x1b[K" );
                    if( idx >= 0 )
                    {
                        var vTarget = versionAndFeedNames[idx].Version;
                        using( monitor.TemporarilySetMinimalFilter( LogFilter.Terse ) )
                        {
                            foreach( var bySolution in byName.GroupBy( s => s.Referer.Solution ) )
                            {
                                var sln = ctx.Solutions.First( s => s.Solution.Solution == bySolution.Key );
                                var updatePackageInfos = bySolution.Select( p => new UpdatePackageInfo( p.Referer,
                                                                                        new ArtifactInstance( p.Target.Artifact, versionAndFeedNames[idx].Version ) ) )
                                                                            .ToList();
                                sln.Driver.UpdatePackageDependencies( monitor, updatePackageInfos, null );
                            }
                        }
                        Console.WriteLine( $"=> {vTarget}" );
                    }
                    else
                    {
                        Console.WriteLine( "=> Skipped." );
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
            m.SetInteractiveUserFilter( new LogClamper( userLevel, true ) );
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
