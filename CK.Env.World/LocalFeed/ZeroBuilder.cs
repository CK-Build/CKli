using CK.Core;
using CK.Env.DependencyModel;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.Env
{
    class ZeroBuilder
    {
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly IWorldSolutionContext _context;
        readonly NormalizedPath _memPath;
        readonly Dictionary<string, HashSet<string>> _sha1Cache;
        readonly HashSet<string> _mustBuild;
        readonly string[] _currentShas;
        readonly HashSet<string> _allShas;

        IReadOnlyList<ZeroBuildProjectInfo> ZeroBuildProjects => _context.DependencyContext.BuildProjectsInfo.ZeroBuildProjects;

        /// <summary>
        /// Reads the current Sha and updates the cache with them.
        /// This must be called only when file changes are, by design, not changing anything to
        /// the build projects' executable.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void RegisterSHAlias( IActivityMonitor m )
        {
            using( m.OpenInfo( "Registering Sha signatures aliases." ) )
            {
                foreach( var p in ZeroBuildProjects )
                {
                    _currentShas[p.Index] = _context.FindDriver( p.Project ).GitRepository.Head.GetSha( p.Project.SolutionRelativeFolderPath );
                    AddCurrentShaToCache( m, p );
                }
                SaveShaCache( m );
            }
        }

        /// <summary>
        /// Runs the builder: publishes the build projects that needs to be.
        /// This is private: <see cref="EnsureZeroBuildProjects"/> calls it.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="mustReloadSolutions">True if solutions must be reloaded.</param>
        /// <returns>True on success, false on error.</returns>
        bool Run( IActivityMonitor m, IBasicApplicationLifetime appLife, out bool mustReloadSolutions )
        {
            Debug.Assert( _mustBuild.Count == ZeroBuildProjects.Count );
            ReadCurrentSha( m );
            Debug.Assert( ZeroBuildProjects.Select( p => _context.FindDriver( p.Project ) )
                                           .All( d => d.GitRepository.CheckCleanCommit( m ) ),
                          "Repositories are clean." );

            mustReloadSolutions = false;
            try
            {
                using( m.OpenTrace( "Analysing dependencies." ) )
                {
                    foreach( var p in ZeroBuildProjects )
                    {
                        using( m.OpenInfo( $"{p} <= {(p.AllDependencies.Any() ? p.AllDependencies.Select( d => d.Name ).Concatenate() : "(no dependency)")}." ) )
                        {
                            var driver = _context.FindDriver( p.Project );

                            // Check cache.
                            var currentTreeSha = _currentShas[p.Index];
                            if( currentTreeSha == null )
                            {
                                throw new Exception( $"Unable to get Sha for {p}." );
                            }
                            if( !_sha1Cache.TryGetValue( p.Project.FullFolderPath, out var shaList ) )
                            {
                                m.Info( $"ReasonToBuild#1: No cached Sha signature found for {p.Project.FullFolderPath}." );
                            }
                            else if( !shaList.Contains( currentTreeSha ) )
                            {
                                m.Info( $"ReasonToBuild#2: Current Sha signature differs from the cached ones." );
                            }
                            else if( p.AllDependencies.Any( dep => _mustBuild.Contains( dep.FullFolderPath ) ) )
                            {
                                m.Info( $"ReasonToBuild#3: Rebuild dependencies are {_mustBuild.Intersect( p.AllDependencies.Select( dep => dep.FullFolderPath.Path ) ).Concatenate()}." );
                            }
                            else if( p.MustPack
                                     && !System.IO.File.Exists(
                                            System.IO.Path.Combine(
                                                _localFeedProvider.ZeroBuild.PhysicalPath,
                                                p.Project.SimpleProjectName + ".0.0.0-0.nupkg" ) ) )
                            {
                                m.Info( $"ReasonToBuild#4: {p.Project.SimpleProjectName}.0.0.0-0 does not exist in in Zero build feed." );
                            }
                            else if( p.Project.IsBuildProject
                                     && !System.IO.File.Exists( _localFeedProvider.GetZeroVersionCodeCakeBuilderExecutablePath( p.Project.Solution.Name ) ) )
                            {
                                m.Info( $"ReasonToBuild#5: Published ZeroVersion CodeCakeBuilder is missing." );
                            }
                            else
                            {
                                _mustBuild.Remove( p.Project.FullFolderPath );
                                m.CloseGroup( $"Project '{p}' is up to date. Build skipped." );
                            }
                        }
                        if( appLife.StopRequested( m ) ) return false;
                    }
                }
                if( _mustBuild.Count == 0 )
                {
                    m.Info( "Nothing to build. Build projects are up-to-date." );
                    mustReloadSolutions = false;
                }
                else
                {
                    mustReloadSolutions = true;
                    using( m.OpenTrace( $"Build/Publish {_mustBuild.Count} build projects: {_mustBuild.Concatenate()}" ) )
                    {
                        foreach( var p in ZeroBuildProjects.Where( p => _mustBuild.Contains( p.Project.FullFolderPath ) ) )
                        {
                            var action = p.MustPack ? "Publishing" : "Building";
                            using( m.OpenInfo( $"{action} {p}." ) )
                            {
                                var driver = _context.FindDriver( p.Project );
                                if( !driver.ZeroBuildProject( m, p ) )
                                {
                                    _sha1Cache.Remove( p.Project.FullFolderPath );
                                    m.CloseGroup( "Failed." );
                                    return false;
                                }
                                _mustBuild.Remove( p.Project.FullFolderPath );
                                AddCurrentShaToCache( m, p );
                                m.CloseGroup( "Success." );
                            }
                            if( appLife.StopRequested( m ) ) return false;
                        }
                    }
                }
                return true;
            }
            finally
            {
                if( mustReloadSolutions )
                {
                    SaveShaCache( m );
                }
                Debug.Assert( ZeroBuildProjects.Select( p => _context.FindDriver( p.Project ) )
                                               .All( d => d.GitRepository.CheckCleanCommit( m ) ),
                              "Repositories are clean." );
            }
        }

        void ReadCurrentSha( IActivityMonitor m )
        {
            using( m.OpenTrace( "Reading current Sha signatures." ) )
            {
                foreach( var p in _context.DependencyContext.BuildProjectsInfo.ZeroBuildProjects )
                {
                    var d = _context.FindDriver( p.Project );
                    Debug.Assert( d != null );
                    _currentShas[p.Index] = d.GitRepository.Head.GetSha( p.Project.SolutionRelativeFolderPath );
                }
            }
        }

        void AddCurrentShaToCache( IActivityMonitor m, ZeroBuildProjectInfo p )
        {
            if( !_sha1Cache.TryGetValue( p.Project.FullFolderPath, out var shaList ) )
            {
                _sha1Cache.Add( p.Project.FullFolderPath, shaList = new HashSet<string>() );
            }
            if( shaList.Add( _currentShas[p.Index] ) && shaList.Count > 1 )
            {
                m.Trace( $"Added new Shalias for {p.Project.FullFolderPath}." );
            }
        }

        void SaveShaCache( IActivityMonitor m )
        {
            m.Trace( $"Saving {_sha1Cache.Count} entries in file '{_memPath}'." );
            System.IO.File.WriteAllLines( _memPath, _sha1Cache.Select( kv => kv.Key + ' ' + kv.Value.Concatenate( "|" ) ) );
        }

        /// <summary>
        /// Encapsulates creation, initalization and run of the builds.
        /// Solutions are soon as version updates have been made.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="feeds">The local feeds.</param>
        /// <param name="ctx">The world solution context to consider.</param>
        /// <returns>The ZeroBuilder on success, null on error.</returns>
        public static ZeroBuilder EnsureZeroBuildProjects(
            IActivityMonitor m,
            IEnvLocalFeedProvider feeds,
            IWorldSolutionContext ctx,
            IBasicApplicationLifetime appLife )
        {
            using( m.OpenInfo( $"Building ZeroVersion projects." ) )
            {
                var builder = Create( m, feeds, ctx );
                if( builder == null ) return null;
                bool success = builder.Run( m, appLife, out bool mustReloadSolutions );
                if( mustReloadSolutions ) ctx.Refresh( m, true );
                return success ? builder : null;
            }
        }

        ZeroBuilder(
            IEnvLocalFeedProvider localFeedProvider,
            NormalizedPath memPath,
            Dictionary<string, HashSet<string>> sha1Cache,
            HashSet<string> initialMustBuild,
            IWorldSolutionContext ctx )
        {
            _localFeedProvider = localFeedProvider;
            _memPath = memPath;
            _sha1Cache = sha1Cache;
            _mustBuild = initialMustBuild;
            _context = ctx;
            _currentShas = new string[ctx.DependencyContext.BuildProjectsInfo.ZeroBuildProjects.Count];
            _allShas = new HashSet<string>();
        }

        /// <summary>
        /// Creates a ZeroBuilder.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="feeds">The local feeds.</param>
        /// <param name="depContext">The dependency context to consider.</param>
        /// <param name="driverFinder">The driver finder by solution name.</param>
        /// <param name="solutionReloader">Optional solutions reloader.</param>
        /// <returns>The ZeroBuilder on success, null on error.</returns>
        static ZeroBuilder Create(
            IActivityMonitor m,
            IEnvLocalFeedProvider feeds,
            IWorldSolutionContext context )
        {
            if( context.DependencyContext.BuildProjectsInfo.HasError )
            {
                using( m.OpenError( "Build Projects dependencies failed to be computed." ) )
                {
                    context.DependencyContext.BuildProjectsInfo.RawBuildProjectsInfoSorterResult.LogError( m );
                }
                return null;
            }
            var zeroProjects = context.DependencyContext.BuildProjectsInfo.ZeroBuildProjects;
            if( zeroProjects.Count == 0 )
            {
                m.Error( context.DependencyContext.HasError ? "Invalid dependency analysis." : "No Build Project exist." );
                return null;
            }
            var mustBuild = new HashSet<string>( zeroProjects.Select( p => p.Project.FullFolderPath.Path ) );
            var memPath = feeds.ZeroBuild.PhysicalPath.AppendPart( "CacheZeroVersion.txt" );
            var sha1Cache = System.IO.File.Exists( memPath )
                            ? System.IO.File.ReadAllLines( memPath )
                                            .Select( l => l.Split() )
                                            .Where( l => mustBuild.Contains( l[0] ) )
                                            .ToDictionary( l => l[0], l => new HashSet<string>( l[1].Split( '|' ) ) )
                            : new Dictionary<string, HashSet<string>>();
            m.Info( $"File '{memPath}' contains {sha1Cache.Count} entries." );
            var currentShas = new string[zeroProjects.Count];

            return new ZeroBuilder( feeds, memPath, sha1Cache, mustBuild, context );
        }

    }
}
