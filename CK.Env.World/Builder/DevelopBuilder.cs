using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CK.Core;
using CSemVer;

namespace CK.Env
{
    class DevelopBuilder : Builder
    {
        readonly string[] _commits;
        readonly bool _withUnitTest;

        public DevelopBuilder(
            ZeroBuilder zeroBuilder,
            ArtifactCenter artifacts,
            IEnvLocalFeedProvider localFeedProvider,
            IDependentSolutionContext ctx,
            Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder,
            bool withUnitTest )
            : base( zeroBuilder, BuildResultType.CI, artifacts, localFeedProvider, ctx, driverFinder )
        {
            _commits = new string[ctx.Solutions.Count];
            _withUnitTest = withUnitTest;
        }

        protected override (SVersion Version, bool MustBuild) PrepareBuild( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( !driver.UpdatePackageDependencies( m, upgrades ) ) return (null,false);
            // A commit is not necessarily created here.
            if( !driver.GitRepository.Commit( m, "Global Build commit." ) ) return (null,false);
            _commits[s.Index] = driver.GitRepository.Head.CommitSha;
            return (driver.GitRepository.GetCommitVersionInfo( m, s.BranchName ).AssemblyBuildInfo.NuGetVersion, true);
        }

        protected override BuildState Build( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IEnumerable<UpdatePackageInfo> buildProjectsUpgrade )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( _commits[s.Index] != driver.GitRepository.Head.CommitSha )
            {
                m.Error( $"Commit changed between PrepareBuild call and this Build. Build canceled." );
                return BuildState.Failed;
            }
            if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return BuildState.Failed;
            if( driver.GitRepository.CanAmendCommit )
            {
                if( !driver.GitRepository.AmendCommit( m ) ) return BuildState.Failed;
            }
            else
            {
                if( !driver.GitRepository.Commit( m, "Required Build commit (for CI): build dependencies changed." ) ) return BuildState.Failed;
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

    }
}
