using CK.Core;
using CK.Env.DependencyModel;
using CSemVer;
using System;
using System.Collections.Generic;

namespace CK.Env
{
    /// <summary>
    /// Builder in CI for develop-local.
    /// Versions are time-based and commits are always amended if possible.
    /// </summary>
    class LocalBuilder : Builder
    {
        readonly DateTimeOffset[] _commitTimes;
        readonly bool _withUnitTest;

        public LocalBuilder(
                ZeroBuilder zeroBuilder,
                ArtifactCenter artifacts,
                IEnvLocalFeedProvider localFeedProvider,
                IWorldSolutionContext ctx,
                bool withUnitTest )
            : base( zeroBuilder, BuildResultType.Local, artifacts, localFeedProvider, ctx )
        {
            _withUnitTest = withUnitTest;
            _commitTimes = new DateTimeOffset[ctx.Solutions.Count];
        }

        protected override (SVersion Version, bool MustBuild) PrepareBuild( IActivityMonitor m, DependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades )
        {
            if( !driver.UpdatePackageDependencies( m, upgrades ) ) return (null, false);
            if( !driver.GitRepository.AmendCommit( m ) ) return (null, false);
            _commitTimes[s.Index] = driver.GitRepository.Head.CommitDate;
            return (driver.GitRepository.ReadVersionInfo( m ).FinalBuildInfo.Version, true);
        }

        protected override BuildState Build( IActivityMonitor m, DependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IReadOnlyCollection<UpdatePackageInfo> buildProjectsUpgrade )
        {
            if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return BuildState.Failed;
            if( !driver.GitRepository.AmendCommit( m, null, date => _commitTimes[s.Index] ) ) return BuildState.Failed;
            return driver.Build( m, withUnitTest: _withUnitTest, withZeroBuilder: true, withPushToRemote: false )
                    ? BuildState.Succeed
                    : BuildState.Failed;
        }

    }
}
