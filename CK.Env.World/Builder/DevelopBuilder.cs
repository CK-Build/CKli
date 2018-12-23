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
            ArtifactCenter artifacts,
            IDependentSolutionContext ctx,
            Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder,
            bool withUnitTest )
            : base( BuildResultType.CI, artifacts, ctx, driverFinder )
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

        protected override bool Build( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades, SVersion sVersion, IEnumerable<UpdatePackageInfo> buildProjectsUpgrade )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );
            if( _commits[s.Index] != driver.GitRepository.Head.CommitSha )
            {
                m.Error( $"Commit changed between PrepareBuild call and this Build. Build canceled." );
                return false;
            }
            if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return false;
            if( driver.GitRepository.CanAmendCommit )
            {
                if( !driver.GitRepository.AmendCommit( m ) ) return false;
            }
            else
            {
                if( !driver.GitRepository.Commit( m, "Required Build commit (for CI)." ) ) return false;
                if( _commits[s.Index] != driver.GitRepository.Head.CommitSha )
                {
                    m.Error( "A required commit has been created because build dependencies changed whereas normal ones didn't and commit cannot be amended. AllBuild can be retried." );
                    return false;
                }
            }
            return driver.Build( m, _withUnitTest, withZeroBuilder: true, withPushToRemote:false );
        }

    }
}
