using CK.Core;
using CK.Env.DependencyModel;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Template method pattern to handle whole stack build.
    /// </summary>
    abstract class Builder
    {
        protected readonly ZeroBuilder ZeroBuilder;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly BuildResultType _type;
        readonly Dictionary<Artifact, SVersion> _packagesVersion;
        readonly List<UpdatePackageInfo>[] _upgrades;
        readonly SVersion[] _targetVersions;
        readonly ArtifactCenter _artifacts;

        protected Builder(
            ZeroBuilder zeroBuilder,
            BuildResultType type,
            ArtifactCenter artifacts,
            IEnvLocalFeedProvider localFeedProvider,
            IWorldSolutionContext ctx )
        {
            ZeroBuilder = zeroBuilder ?? throw new ArgumentNullException( nameof( zeroBuilder ) );
            _type = type;
            _artifacts = artifacts ?? throw new ArgumentNullException( nameof( artifacts ) );
            _localFeedProvider = localFeedProvider ?? throw new ArgumentNullException( nameof( localFeedProvider ) );
            DependentSolutionContext = ctx ?? throw new ArgumentNullException( nameof( ctx ) );
            _packagesVersion = new Dictionary<Artifact, SVersion>();
            _upgrades = new List<UpdatePackageInfo>[ctx.Solutions.Count];
            _targetVersions = new SVersion[ctx.Solutions.Count];
        }

        /// <summary>
        /// Gets the solution context.
        /// </summary>
        public IWorldSolutionContext DependentSolutionContext { get; private set; }

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
                    // This is not really optimal but it is required only for DevelopBuilder
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
                result = CreateBuildResult( m );
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

        /// <summary>
        /// Creates the <see cref="BuildResult"/> once <see cref="PrepareBuild"/> has been called
        /// for each solutions.
        /// ReleaseBuilder overrides this to apply build project upgrades before its actual Build.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The build result. Null on error.</returns>
        protected virtual BuildResult CreateBuildResult( IActivityMonitor m )
        {
            return BuildResult.Create( m, _type, _artifacts, DependentSolutionContext, _targetVersions, GetReleaseNotes() );
        }

        /// <summary>
        /// Gets the release notes. Only ReleaseBuilder overrides this to return the release notes
        /// from the roadmap.
        /// </summary>
        /// <returns>Null or the release notes.</returns>
        protected virtual IReadOnlyList<ReleaseNoteInfo> GetReleaseNotes()
        {
            return null;
        }

        /// <summary>
        /// Gets the set of upgrades that must be applied to the build projects of a solution.
        /// This is used by Build for Develop and Local builders but ReleaseBuilder upgrades
        /// the build project dependencies from its PrepareBuild.
        /// </summary>
        /// <param name="s">The solution.</param>
        /// <returns>The build project upgrades that must be applied.</returns>
        protected IReadOnlyCollection<UpdatePackageInfo> GetBuildProjectUpgrades( DependentSolution s )
        {
            return DependentSolutionContext.DependencyContext.BuildProjectsInfo.ZeroBuildProjects
                                         .Where( z => z.Project.Solution == s )
                                         .SelectMany( z => z.UpgradePackages
                                               .Select( a => new UpdatePackageInfo(
                                                   z.Project,
                                                   new ArtifactInstance( a.Type, a.Name, _packagesVersion[a] ) ) ) )
                                         .ToList();
        }

        bool RunPrepareBuild( IActivityMonitor m )
        {
            // Required for DevelopBuilder retry.
            _packagesVersion.Clear();
            var solutionAndDrivers = DependentSolutionContext.Solutions;
            for( int i = 0; i < solutionAndDrivers.Count; ++i )
            {
                var s = solutionAndDrivers[i];
                var upgrades = s.Solution.ImportedLocalPackages
                                .Select( p => new UpdatePackageInfo(
                                                    p.Importer,
                                                    new ArtifactInstance( p.Package.Artifact.Type, p.Package.Artifact.Name, _packagesVersion[p.Package.Artifact] ) ) )
                                .ToList();
                _upgrades[i] = upgrades;
                using( m.OpenInfo( $"Preparing {s} build." ) )
                {
                    var pr = PrepareBuild( m, s.Solution, s.Driver, upgrades );
                    ZeroBuilder.RegisterSHAlias( m );
                    if( pr.Version == null ) return false;
                    m.CloseGroup( $"Target version: {pr.Version}{(pr.MustBuild ? "" : " (no build required)")}" );
                    _targetVersions[i] = pr.MustBuild ? pr.Version : null;
                    _packagesVersion.AddRange( s.Solution.Solution.GeneratedArtifacts.Select( p => new KeyValuePair<Artifact, SVersion>( p.Artifact, pr.Version ) ) );
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
            foreach( var (s, d) in DependentSolutionContext.Solutions )
            {
                DependentSolutionContext.DependencyContext.LogSolutions( m, s );
                IReadOnlyCollection<UpdatePackageInfo> buildProjectsUpgrade = GetBuildProjectUpgrades( s );
                using( m.OpenInfo( $"Running {s} build." ) )
                {
                    // _targetVersions[i] is null if build must not be done (this is for ReleaseBuilder only).
                    // We use the null version also for DevelopBuilder: 
                    var sVersion = finalState == BuildState.MustRetry ? null : _targetVersions[s.Index];
                    finalState = Build( m, s, d, _upgrades[s.Index], sVersion, buildProjectsUpgrade );
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
        protected abstract (SVersion Version, bool MustBuild) PrepareBuild( IActivityMonitor m, DependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades );

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
        protected abstract BuildState Build( IActivityMonitor m, DependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IReadOnlyCollection<UpdatePackageInfo> buildProjectsUpgrade );

    }
}
