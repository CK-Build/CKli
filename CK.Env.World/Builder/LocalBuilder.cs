using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CK.Core;
using CSemVer;

namespace CK.Env
{
    class LocalBuilder : Builder
    {
        readonly DateTimeOffset[] _commitTimes;
        readonly bool _withUnitTest;

        public LocalBuilder(
                ZeroBuilder zeroBuilder,
                ArtifactCenter artifacts,
                IEnvLocalFeedProvider localFeedProvider,
               IDependentSolutionContext ctx,
                Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder,
                bool withUnitTest )
            : base( zeroBuilder, BuildResultType.Local, artifacts, localFeedProvider, ctx, driverFinder )
        {
            _withUnitTest = withUnitTest;
            _commitTimes = new DateTimeOffset[ ctx.Solutions.Count ];
        }

        protected override (SVersion Version, bool MustBuild) PrepareBuild( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( !driver.UpdatePackageDependencies( m, upgrades ) ) return (null,false);
            if( !driver.GitRepository.AmendCommit( m ) ) return (null,false);
            _commitTimes[s.Index] = driver.GitRepository.Head.CommitDate;
            return (driver.GitRepository.GetCommitVersionInfo( m, s.BranchName ).AssemblyBuildInfo.NuGetVersion,true);
        }

        protected override BuildState Build( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IEnumerable<UpdatePackageInfo> buildProjectsUpgrade )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return BuildState.Failed;
            if( !driver.GitRepository.AmendCommit( m, null, date => _commitTimes[s.Index] ) ) return BuildState.Failed;
            return driver.Build( m, withUnitTest: _withUnitTest, withZeroBuilder: true, withPushToRemote: false )
                    ? BuildState.Succeed
                    : BuildState.Failed;
        }

    }
}
