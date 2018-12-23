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
                ArtifactCenter artifacts,
                IDependentSolutionContext ctx,
                Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder,
                bool withUnitTest )
            : base( BuildResultType.Local, artifacts, ctx, driverFinder )
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

        protected override bool Build( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IEnumerable<UpdatePackageInfo> buildProjectsUpgrade )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return false;
            if( !driver.GitRepository.AmendCommit( m, null, date => _commitTimes[s.Index] ) ) return false;
            return driver.Build( m, withUnitTest: _withUnitTest, withZeroBuilder: true, withPushToRemote: false );
        }

    }
}
