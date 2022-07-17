using CK.Core;
using CK.Env.DependencyModel;

using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// BUilder for CI in develop.
    /// Versions are based on the number of commits from head to the last actual release.
    /// Builds may be retried whenever an update of the build chain is required and the commit
    /// cannot be amended (fresh checkout or right after a push).
    /// This is a edge case that doesn't happen often.
    /// </summary>
    class DevelopBuilder : Builder
    {
        readonly string[] _commits;
        readonly bool _withUnitTest;

        public DevelopBuilder( ZeroBuilder zeroBuilder,
                               ArtifactCenter artifacts,
                               IEnvLocalFeedProvider localFeedProvider,
                               IWorldSolutionContext ctx,
                               bool withUnitTest )
            : base( zeroBuilder, BuildResultType.CI, artifacts, localFeedProvider, ctx )
        {
            _commits = new string[ctx.Solutions.Count];
            _withUnitTest = withUnitTest;
        }

        protected override (SVersion? Version, bool MustBuild) PrepareBuild( IActivityMonitor m,
                                                                             DependentSolution s,
                                                                             ISolutionDriver driver,
                                                                             IReadOnlyList<UpdatePackageInfo> upgrades )
        {
            if( !driver.UpdatePackageDependencies( m, upgrades ) ) return (null, false);

            // Note that a commit is not necessarily created here if the dependencies have not changed!
            // (When working folder is up-to-date.)
            var upText = upgrades.Select( u => u.PackageUpdate.ToString() ).Concatenate( Environment.NewLine );
            var msg = $"CI build: Upgrading dependencies:{Environment.NewLine}{Environment.NewLine}{upText}.";
            if( driver.GitRepository.Commit( m, msg ) == CommittingResult.Error ) return (null, false);
            _commits[s.Index] = driver.GitRepository.Head.CommitSha;
            return (driver.GitRepository.ReadVersionInfo( m )?.Commit.FinalBuildInfo.Version, true);
        }

        protected override BuildState Build( IActivityMonitor m,
                                             DependentSolution s,
                                             ISolutionDriver driver,
                                             IReadOnlyList<UpdatePackageInfo> upgrades,
                                             SVersion sVersion,
                                             IReadOnlyCollection<UpdatePackageInfo> buildProjectsUpgrade )
        {
            if( _commits[s.Index] != driver.GitRepository.Head.CommitSha )
            {
                m.Error( $"Commit changed between PrepareBuild call and this Build. Build canceled." );
                return BuildState.Failed;
            }
            if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return BuildState.Failed;
            if( driver.GitRepository.CanAmendCommit )
            {
                if( driver.GitRepository.AmendCommit( m ) == CommittingResult.Error ) return BuildState.Failed;
            }
            else
            {
                if( driver.GitRepository.Commit( m, "Required Build commit (for CI): build dependencies changed." )
                    == CommittingResult.Error )
                {
                    return BuildState.Failed;
                }
                var currentSha = driver.GitRepository.Head.CommitSha;
                if( _commits[s.Index] != currentSha )
                {
                    m.Warn( "A required commit has been created because build dependencies changed whereas normal ones didn't and commit cannot be amended. Build will be be retried." );
                    _commits[s.Index] = currentSha;
                    return BuildState.MustRetry;
                }
            }
            if( sVersion == null )
            {
                m.Trace( "Retry mode: skipping actual build." );
                return BuildState.MustRetry;
            }
            return driver.Build( m, _withUnitTest, withZeroBuilder: true, withPushToRemote: false )
                    ? BuildState.Succeed
                    : BuildState.Failed;
        }

        protected override void OnSolutionBuildFailed( IActivityMonitor monitor, DependentSolution s )
        {
            var solutions = DependentSolutionContext.DependentSolutions;
            int resetIndex = s.Index + 1;
            if( resetIndex < solutions.Count )
            {
                using( monitor.OpenInfo( $"Restoring states of following {solutions.Count - resetIndex} Git folders." ) )
                {
                    for( int i = resetIndex; i < solutions.Count; i++ )
                    {
                        var g = DependentSolutionContext.Drivers[i].GitRepository;
                        g.ResetBranchState( monitor, g.CurrentBranchName, _commits[i] );
                    }

                }
            }
        }


    }
}
