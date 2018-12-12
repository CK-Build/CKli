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
            IDependentSolutionContext ctx,
            Func<IActivityMonitor,IDependentSolution,ISolutionDriver> driverFinder,
            bool withUnitTest )
            : base( ctx, driverFinder )
        {
            _withUnitTest = withUnitTest;
            _commitTimes = new DateTimeOffset[ ctx.Solutions.Count ];
        }

        protected override SVersion PrepareBuild( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( !driver.UpdatePackageDependencies( m, upgrades ) ) return null;
            if( !driver.GitRepository.AmendCommit( m ) ) return null;
            _commitTimes[s.Index] = driver.GitRepository.Head.CommitDate;
            return driver.GitRepository.GetCommitVersionInfo( m, s.BranchName ).AssemblyBuildInfo.NuGetVersion;
        }

        protected override bool Build( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IEnumerable<UpdatePackageInfo> buildProjectsUpgrade )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return false;
            if( !driver.GitRepository.AmendCommit( m, null, date => _commitTimes[s.Index] ) ) return false;
            return driver.Build( m, withUnitTest: _withUnitTest, withZeroBuilder: true, withPushToRemote: false );
        }

    }
}
