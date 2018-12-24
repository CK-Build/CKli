using CK.Core;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CK.Env
{
    class ZeroBuilder
    {
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> _driverFinder;
        readonly NormalizedPath _memPath;
        readonly Dictionary<string, HashSet<string>> _sha1Cache;
        readonly HashSet<string> _mustBuild;
        readonly IDependentSolutionContext _depContext;
        readonly string[] _currentShas;
        readonly Dictionary<string, ISolutionDriver> _driverMap;
        readonly HashSet<string> _allShas;

        /// <summary>
        /// Reads the current Sha and updates the cache with them.
        /// This must be called only when file changes are, by design, not changing anything to
        /// the build projects' executable.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void RegisterSHAlias( IActivityMonitor m )
        {
            bool mustReadSha = true;
            if( !IsInitialized )
            {
                ReadCurrentSha( m );
                mustReadSha = false;
            }
            using( m.OpenInfo( "Registering Sha signatures aliases." ) )
            {
                foreach( var p in _depContext.BuildProjectsInfo )
                {
                    if( mustReadSha ) _currentShas[p.Index] = _driverMap[p.SolutionName].GitRepository.Head.GetSha( p.PrimarySolutionRelativeFolderPath );
                    AddCurrentShaToCache( m, p );
                }
                SaveShaCache( m );
            }
        }

        /// <summary>
        /// Runs the builder: publishes the build projects that needs to be.
        /// This can be called multiple times (internal state is refreshed as needed).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        bool Run( IActivityMonitor m )
        {
            Debug.Assert( _mustBuild.Count == _depContext.BuildProjectsInfo.Count );
            ReadCurrentSha( m );
            var scopeMap = new Dictionary<ISolutionDriver, IDisposable>();
            try
            {
                using( m.OpenTrace( "Analysing dependencies." ) )
                {
                    foreach( var p in _depContext.BuildProjectsInfo )
                    {
                        using( m.OpenInfo( $"{p} <= {(p.Dependencies.Any() ? p.Dependencies.Concatenate() : "(no dependency)")}." ) )
                        {
                            var driver = _driverMap[p.SolutionName];

                            // Check cache.
                            var currentTreeSha = _currentShas[p.Index];
                            if( currentTreeSha == null )
                            {
                                throw new Exception( $"Unable to get Sha for {p.PrimarySolutionRelativeFolderPath}." );
                            }
                            if( !_sha1Cache.TryGetValue( p.FullName, out var shaList ) )
                            {
                                m.Info( $"ReasonToBuild#1: No cached Sha signature found for {p.FullName}." );
                            }
                            else if( !shaList.Contains( currentTreeSha ) )
                            {
                                m.Info( $"ReasonToBuild#2: Current Sha signature differs from the cached ones." );
                            }
                            else if( p.Dependencies.Any( depName => _mustBuild.Contains( depName ) ) )
                            {
                                m.Info( $"ReasonToBuild#3: Rebuild dependencies {_mustBuild.Intersect( p.Dependencies ).Concatenate()}." );
                            }
                            else if( p.MustPack
                                     && _localFeedProvider.ZeroBuild.GetPackageFile( m, p.ProjectName, SVersion.ZeroVersion ) == null )
                            {
                                m.Info( $"ReasonToBuild#4: {p.ProjectName}.0.0.0-0 does not exist in in Zero build feed." );
                            }
                            else
                            {
                                _mustBuild.Remove( p.FullName );
                                m.CloseGroup( $"Project '{p}' is up to date. Build skipped." );
                                continue;
                            }
                        }
                    }
                }
                if( _mustBuild.Count == 0 ) m.Info( "Nothing to build. Build projects are up-to-date." );
                else
                {
                    using( m.OpenTrace( "Creating protected scopes and applying zero dependencies." ) )
                    {
                        foreach( var p in _depContext.BuildProjectsInfo )
                        {
                            using( m.OpenInfo( $"Configuring {p}." ) )
                            {
                                var driver = _driverMap[p.SolutionName];
                                if( !scopeMap.ContainsKey( driver ) )
                                {
                                    scopeMap.Add( driver, driver.GitRepository.OpenProtectedScope( m, null ) );
                                }
                                // Always sets Zero version dependencies even if we don't build it so that
                                // dependent project see homogeneous Zero versions for all its dependencies.
                                var zeroDeps = p.UpgradePackages.Select( dep => new UpdatePackageInfo( p.SolutionName, p.ProjectName, dep, SVersion.ZeroVersion ) );
                                if( !driver.UpdatePackageDependencies( m, zeroDeps ) ) return false;
                            }
                        }
                    }

                    using( m.OpenTrace( $"Build/Publish {_mustBuild.Count} build projects: {_mustBuild.Concatenate()}" ) )
                    {
                        foreach( var p in _depContext.BuildProjectsInfo.Where( p => _mustBuild.Contains( p.FullName ) ) )
                        {
                            var action = p.MustPack ? "Publishing" : "Building";
                            using( m.OpenInfo( $"{action} {p}." ) )
                            {
                                var driver = _driverMap[p.SolutionName];
                                if( !driver.ZeroBuildProject( m, p ) )
                                {
                                    _sha1Cache.Remove( p.FullName );
                                    m.CloseGroup( "Failed." );
                                    return false;
                                }
                                _mustBuild.Remove( p.FullName );
                                AddCurrentShaToCache( m, p );
                                m.CloseGroup( "Success." );
                            }
                        }
                    }
                }
                return true;
            }
            finally
            {
                foreach( var scope in scopeMap.Values ) scope.Dispose();
                SaveShaCache( m );
            }
        }

        bool IsInitialized => _driverMap?.Count > 0;

        void ReadCurrentSha( IActivityMonitor m )
        {
            using( m.OpenTrace( IsInitialized ? "Reading current Sha signatures." : "Resolving drivers and reading Sha signatures." ) )
            {
                foreach( var p in _depContext.BuildProjectsInfo )
                {
                    if( !_driverMap.TryGetValue( p.SolutionName, out var d ) )
                    {
                        d = _driverFinder( m, _depContext, p.SolutionName );
                        Debug.Assert( d != null );
                        _driverMap.Add( p.SolutionName, d );
                    }
                    _currentShas[p.Index] = d.GitRepository.Head.GetSha( p.PrimarySolutionRelativeFolderPath );
                }
            }
        }

        void AddCurrentShaToCache( IActivityMonitor m, ZeroBuildProjectInfo p )
        {
            if( !_sha1Cache.TryGetValue( p.FullName, out var shaList ) )
            {
                _sha1Cache.Add( p.FullName, shaList = new HashSet<string>() );
            }
            if( shaList.Add( _currentShas[p.Index] ) && shaList.Count > 1 )
            {
                m.Trace( $"Added new Shalias for {p.FullName}." );
            }
        }

        void SaveShaCache( IActivityMonitor m )
        {
            m.Trace( $"Saving {_sha1Cache.Count} entries in file '{_memPath}'." );
            System.IO.File.WriteAllLines( _memPath, _sha1Cache.Select( kv => kv.Key + ' ' + kv.Value.Concatenate( "|" ) ) );
        }

        /// <summary>
        /// Encapsulates creation, initalization (<see cref="ReadCurrentSha"/>) and <see cref="Run"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="feeds">The local feeds.</param>
        /// <param name="depContext">The dependency context to consider.</param>
        /// <param name="driverFinder">The driver finder by solution name.</param>
        /// <param name="solutionReloader">Optional solutions reloader.</param>
        /// <returns>The ZeroBuilder on success, null on error.</returns>
        public static ZeroBuilder EnsureZeroBuildProjects(
            IActivityMonitor m,
            IEnvLocalFeedProvider feeds,
            IDependentSolutionContext depContext,
            Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder,
            Func<IActivityMonitor,bool> solutionReloader )
        {
            using( m.OpenInfo( $"Building ZeroVersion projects." ) )
            {
                var builder = ZeroBuilder.Create( m, feeds, depContext, driverFinder, solutionReloader );
                if( builder == null ) return null;
                bool success = builder.Run( m );
                if( solutionReloader != null ) success &= solutionReloader.Invoke( m );
                return success ? builder : null;
            }
        }

        ZeroBuilder(
            IEnvLocalFeedProvider localFeedProvider,
            Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder,
            NormalizedPath memPath,
            Dictionary<string, HashSet<string>> sha1Cache,
            HashSet<string> initialMustBuild,
            IDependentSolutionContext depContext )
        {
            _localFeedProvider = localFeedProvider;
            _driverFinder = driverFinder;
            _memPath = memPath;
            _sha1Cache = sha1Cache;
            _mustBuild = initialMustBuild;
            _depContext = depContext;
            _currentShas = new string[depContext.BuildProjectsInfo.Count];
            _driverMap = new Dictionary<string, ISolutionDriver>();
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
            IDependentSolutionContext depContext,
            Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder,
            Func<IActivityMonitor, bool> solutionReloader )
        {
            if( depContext.BuildProjectsInfo == null )
            {
                m.Error( "Build Projects dependencies failed to be computed." );
                return null;
            }
            if( depContext.BuildProjectsInfo.Count == 0 )
            {
                m.Error( "No Build Project exist." );
                return null;
            }
            var mustBuild = new HashSet<string>( depContext.BuildProjectsInfo.Select( z => z.FullName ) );
            var memPath = feeds.ZeroBuild.PhysicalPath.AppendPart( "CacheZeroVersion.txt" );
            var sha1Cache = System.IO.File.Exists( memPath )
                            ? System.IO.File.ReadAllLines( memPath )
                                            .Select( l => l.Split() )
                                            .Where( l => mustBuild.Contains( l[0] ) )
                                            .ToDictionary( l => l[0], l => new HashSet<string>( l[1].Split( '|' ) ) )
                            : new Dictionary<string, HashSet<string>>();
            m.Info( $"File '{memPath}' contains {sha1Cache.Count} entries." );
            var currentShas = new string[depContext.BuildProjectsInfo.Count];

            return new ZeroBuilder( feeds, driverFinder, memPath, sha1Cache, mustBuild, depContext );
        }

    }
}
