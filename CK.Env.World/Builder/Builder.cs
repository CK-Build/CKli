using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Template method pattern to handle whole stack build.
    /// </summary>
    abstract class Builder
    {
        protected readonly Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> DriverFinder;
        protected readonly ZeroBuilder ZeroBuilder;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly BuildResultType _type;
        readonly ISolutionDriver[] _drivers;
        readonly Dictionary<string, SVersion> _packagesVersion;
        readonly List<UpdatePackageInfo>[] _upgrades;
        readonly SVersion[] _targetVersions;
        readonly ArtifactCenter _artifacts;

        protected Builder(
            ZeroBuilder zeroBuilder,
            BuildResultType type,
            ArtifactCenter artifacts,
            IEnvLocalFeedProvider localFeedProvider,
            IDependentSolutionContext ctx,
            Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder )
        {
            if( zeroBuilder == null ) throw new ArgumentNullException( nameof( zeroBuilder ) );
            if( ctx == null ) throw new ArgumentNullException( nameof( ctx ) );
            if( driverFinder == null ) throw new ArgumentNullException( nameof( driverFinder ) );
            if( artifacts == null ) throw new ArgumentNullException( nameof( artifacts ) );
            if( localFeedProvider == null ) throw new ArgumentNullException( nameof( localFeedProvider ) );

            ZeroBuilder = zeroBuilder;
            _type = type;
            _artifacts = artifacts;
            _localFeedProvider = localFeedProvider;
            DependentSolutionContext = ctx;
            DriverFinder = driverFinder;
            _packagesVersion = new Dictionary<string, SVersion>();
            _upgrades = new List<UpdatePackageInfo>[ctx.Solutions.Count];
            _targetVersions = new SVersion[ctx.Solutions.Count];
            _drivers = new ISolutionDriver[ctx.Solutions.Count];
        }

        /// <summary>
        /// Gets the dependent solution context.
        /// </summary>
        public IDependentSolutionContext DependentSolutionContext { get; private set; }

        /// <summary>
        /// Runs the build. Orchestrates calls to <see cref="PrepareBuild"/> and <see cref="Build"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The BuildResult on success, null on error.</returns>
        public BuildResult Run( IActivityMonitor m, bool forceRebuild )
        {
            if( _packagesVersion.Count > 0 ) throw new InvalidOperationException();

            BuildResult result = CreateResultByPreparingBuilds( m, forceRebuild );
            if( result == null ) return null;
            using( m.OpenInfo( "Running builds." ) )
            {
                BuildState state = RunBuild( m );
                if( state == BuildState.Failed )
                {
                    m.CloseGroup( "Failed." );
                    return null;
                }
                if( state == BuildState.Succeed )
                {
                    m.CloseGroup( "Success." );
                }
                else
                {
                    // This is not really optimal but it is required only for DeveloPbuilder
                    // (where version numbers are not fully known upfront AND commit may not be
                    // amendable (when on a fresh checkout).
                    // We may have resolved and applied the buildUpgrades in a tight dedicated loop
                    // but we would need to do exactly the same code than the DevelopBuilder.RunBuild does:
                    // applying build upgrades, checks the commit, then calls ReadCommitVersionInfo on actual
                    // (non amended commits), applies this new version to any previous solution and start again...
                    // This is exactly what this whole code does thanks to the BuildState.MustRetry.
                    using( m.OpenInfo( "Retrying running builds." ) )
                    {
                        do
                        {
                            result = CreateResultByPreparingBuilds( m, forceRebuild );
                            state = RunBuild( m );
                        }
                        while( state == BuildState.MustRetry );
                        if( state == BuildState.Failed )
                        {
                            m.CloseGroup( "Retry failed." );
                            m.CloseGroup( "Failed (with retries)." );
                            return null;
                        }
                        if( state == BuildState.Succeed )
                        {
                            m.CloseGroup( "Retry succeed." );
                            m.CloseGroup( "Success (with retries)." );
                        }
                    }
                }
            }
            return result;
        }

        BuildResult CreateResultByPreparingBuilds( IActivityMonitor m, bool forceRebuild )
        {
            BuildResult result = null;
            using( m.OpenInfo( "Preparing builds." ) )
            {
                if( !RunPrepareBuild( m ) )
                {
                    m.CloseGroup( "Failed." );
                    return null;
                }
                result = BuildResult.Create( m, _type, _artifacts, DependentSolutionContext.Solutions, _targetVersions, GetReleaseNotes() );
                if( result == null ) return null;
                if( forceRebuild )
                {
                    using( m.OpenInfo( "Forcing rebuild: Removing already existing artifacts that will be produced from caches." ) )
                    {
                        _localFeedProvider.RemoveFromAllCaches( m, result.GeneratedArtifacts.Select( g => g.Artifact ) );
                        _localFeedProvider.GetFeed( _type ).Remove( m, result.GeneratedArtifacts.Select( g => g.Artifact ) );
                    }
                }
                m.CloseGroup( "Success." );
            }
            return result;
        }

        protected virtual IReadOnlyList<ReleaseNoteInfo> GetReleaseNotes()
        {
            return null;
        }

        bool RunPrepareBuild( IActivityMonitor m )
        {
            // Required for DevelopBuilder retry.
            _packagesVersion.Clear();
            var solutions = DependentSolutionContext.Solutions;
            for( int i = 0; i < solutions.Count; ++i )
            {
                var s = solutions[i];
                var driver = _drivers[i];
                if( driver == null )
                {
                    driver = _drivers[i] = DriverFinder( m, DependentSolutionContext, s.UniqueSolutionName );
                }
                var upgrades = s.ImportedLocalPackages
                                .Select( p => new UpdatePackageInfo(
                                                    s.UniqueSolutionName,
                                                    p.ProjectName,
                                                    p.Package.PackageId,
                                                    _packagesVersion[p.Package.PackageId] ) )
                                .ToList();
                _upgrades[i] = upgrades;
                using( m.OpenInfo( $"Preparing {s} build." ) )
                {
                    var pr = PrepareBuild( m, s, driver, upgrades );
                    ZeroBuilder.RegisterSHAlias( m );
                    if( pr.Version == null ) return false;
                    m.CloseGroup( $"Target version: {pr.Version}{(pr.MustBuild ? "" :" (no build required)")}" );
                    _targetVersions[i] = pr.MustBuild ? pr.Version : null;
                    _packagesVersion.AddRange( s.GeneratedPackages.Select( p => new KeyValuePair<string, SVersion>( p.Name, pr.Version ) ) );
                }
            }
            return true;
        }

        /// <summary>
        /// RunBuild returns. This is for DevelopBuilder since Release and LocalBuilder
        /// only return Succeed or Failed (a boolean would be enough).
        /// </summary>
        protected enum BuildState
        {
            Failed,
            Succeed,
            MustRetry
        }

        BuildState RunBuild( IActivityMonitor m )
        {
            BuildState finalState = BuildState.Succeed;
            var solutions = DependentSolutionContext.Solutions;
            for( int i = 0; i < solutions.Count; ++i )
            {
                var s = solutions[i];
                DependentSolutionContext.LogSolutions( m, s );
                var buildProjectsUpgrade = DependentSolutionContext.BuildProjectsInfo
                                             .Where( z => z.SolutionName == s.UniqueSolutionName )
                                             .SelectMany( z => z.UpgradePackages
                                                   .Select( packageName => new UpdatePackageInfo(
                                                       s.UniqueSolutionName,
                                                       z.ProjectName,
                                                       packageName,
                                                       _packagesVersion[packageName] ) ) );
                using( m.OpenInfo( $"Running {s} build." ) )
                {
                    // _targetVersions[i] is null if build must not be done (this is for ReleaseBuilder only).
                    // We use the null version also for DevelopBuilder: 
                    var sVersion = finalState == BuildState.MustRetry ? null : _targetVersions[i];
                    finalState = Build( m, solutions[i], _drivers[i], _upgrades[i], sVersion, buildProjectsUpgrade );
                    ZeroBuilder.RegisterSHAlias( m );
                    // For DevelopBuilder that returned Retry, we continue to apply the
                    // current change on subsequent solutions to minimize the number of actual builds.
                    if( finalState == BuildState.Failed ) break;
                }
            }
            return finalState;
        }

        /// <summary>
        /// Must prepare the build (typically by applying the <paramref name="upgrades"/>) and returns
        /// a version for the solution (or null on error).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="s">The solution.</param>
        /// <param name="driver">The solution driver.</param>
        /// <param name="upgrades">The set of required package upgrades.</param>
        /// <returns>The version (or null if an error occurred) and whether the build must be actually done or skipped.</returns>
        protected abstract (SVersion Version, bool MustBuild) PrepareBuild( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades );

        /// <summary>
        /// Builds the solution.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="s">The solution.</param>
        /// <param name="driver">The solution driver.</param>
        /// <param name="upgrades">The set of required package upgrades.</param>
        /// <param name="sVersion">The version computed by <see cref="PrepareBuild"/>.</param>
        /// <param name="buildProjectsUpgrade">The build projects upgrades.</param>
        /// <returns>The build state.</returns>
        protected abstract BuildState Build( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IEnumerable<UpdatePackageInfo> buildProjectsUpgrade );

    }
}
