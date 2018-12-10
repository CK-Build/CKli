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

        public DevelopBuilder(
            IDependentSolutionContext ctx,
            Func<IActivityMonitor,IDependentSolution,ISolutionDriver> driverFinder )
            : base( ctx, driverFinder )
        {
            _commits = new string[ctx.Solutions.Count];
        }

        protected override SVersion PrepareBuild( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( !driver.UpdatePackageDependencies( m, upgrades ) ) return null;
            if( !driver.GitRepository.Commit( m, "Global Build commit." ) ) return null;
            _commits[s.Index] = driver.GitRepository.Head.CommitSha;
            return driver.GitRepository.GetCommitVersionInfo( m, s.BranchName ).AssemblyBuildInfo.NuGetVersion;
        }

        protected override bool Build( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IEnumerable<UpdatePackageInfo> buildProjectsUpgrade )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( _commits[s.Index] != driver.GitRepository.Head.CommitSha )
            {
                m.Error( $"Commit changed between PrepareBuild call and this Build. Build canceled." );
                return false;
            }
            if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return false;
            if( !driver.GitRepository.AmendCommit( m ) ) return false;
            return driver.Build( m, false );
        }

    }
}
