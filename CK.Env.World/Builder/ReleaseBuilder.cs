using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CK.Core;
using CK.Text;
using CSemVer;

namespace CK.Env
{
    class ReleaseBuilder : Builder
    {
        readonly string[] _commits;
        readonly ReleaseRoadmap _roadmap;

        public ReleaseBuilder(
                ZeroBuilder zeroBuilder,
                ArtifactCenter artifacts,
                ReleaseRoadmap roadmap,
                IEnvLocalFeedProvider localFeedProvider,
                Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder )
            : base( zeroBuilder, BuildResultType.Release, artifacts, localFeedProvider, roadmap.DependentSolutionContext, driverFinder )
        {
            _commits = new string[roadmap.DependentSolutionContext.Solutions.Count];
            _roadmap = roadmap;
        }


        protected override (SVersion Version, bool MustBuild) PrepareBuild(
            IActivityMonitor m,
            IDependentSolution s,
            ISolutionDriver driver,
            IReadOnlyList<UpdatePackageInfo> upgrades )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );

            IReleaseSolutionInfo info = _roadmap.ReleaseInfos[ s.Index ];
            var targetVersion = info.CurrentReleaseInfo.Version;
            if( upgrades.Count > 0 )
            {
                if( !driver.UpdatePackageDependencies( m, upgrades ) ) return (null,false);
                var upText = upgrades.Select( u => u.PackageUpdate.ToString() ).Concatenate();
                var msg = info.CurrentReleaseInfo.Level == ReleaseLevel.None
                            ? $"Not released (keeping version {targetVersion}). Upgrading release dependencies: {upText}."
                            : $"Releasing {targetVersion}. Upgrading release dependencies: {upText}." + Environment.NewLine
                              + Environment.NewLine
                              + info.ReleaseNote;
                if( !driver.GitRepository.Commit( m, msg ) ) return (null,false);
            }
            _commits[s.Index] = driver.GitRepository.Head.CommitSha;
            return (targetVersion, info.CurrentReleaseInfo.Level != ReleaseLevel.None);
        }

        protected override BuildResult CreateBuildResult( IActivityMonitor m, IReadOnlyList<ISolutionDriver> drivers )
        {
            foreach( var s in DependentSolutionContext.Solutions )
            {
                var buildProjectUpgrades = GetBuildProjectUpgrades( s );
                var driver = drivers[s.Index];
                if( !driver.UpdatePackageDependencies( m, buildProjectUpgrades ) ) return null;
                if( !driver.GitRepository.Commit( m, "Updated Build project dependencies.", amendIfPossible: true ) ) return null;
                _commits[s.Index] = driver.GitRepository.Head.CommitSha;
            }
            return base.CreateBuildResult( m, drivers );
        }

        protected override IReadOnlyList<ReleaseNoteInfo> GetReleaseNotes() => _roadmap.GetReleaseNotes();

        protected override BuildState Build(
            IActivityMonitor m,
            IDependentSolution s,
            ISolutionDriver driver,
            IReadOnlyList<UpdatePackageInfo> upgrades,
            SVersion sVersion,
            IEnumerable<UpdatePackageInfo> buildProjectsUpgrade )
        {
            IReleaseSolutionInfo info = _roadmap.ReleaseInfos[s.Index];
            Debug.Assert( (sVersion == null) == (info.CurrentReleaseInfo.Level == ReleaseLevel.None) );
            var targetVersion = info.CurrentReleaseInfo.Version;
            Debug.Assert( sVersion == null || sVersion == targetVersion );

            if( _commits[s.Index] != driver.GitRepository.Head.CommitSha )
            {
                m.Error( $"Commit changed between CreateBuildResult call and this Build. Build canceled." );
                return BuildState.Failed;
            }
            if( sVersion == null )
            {
                m.Info( $"Build skipped for {s.UniqueSolutionName}." );
                return BuildState.Succeed;
            }
            else
            {
                bool buildResult = DoBuild( m, driver, targetVersion, out bool tagCreated );
                if( !buildResult && tagCreated )
                {
                    driver.GitRepository.ClearVersionTag( m, targetVersion );
                }
                if( targetVersion.PackageQuality == PackageQuality.Release )
                {
                    buildResult &= driver.GitRepository.SwitchMasterToDevelop( m );
                }
                return buildResult ? BuildState.Succeed : BuildState.Failed;
            }
        }

        private static bool DoBuild(
            IActivityMonitor m,
            ISolutionDriver driver,
            CSVersion targetVersion,
            out bool tagCreated )
        {
            tagCreated = false;
            try
            {
                var git = driver.GitRepository;
                if( targetVersion.PackageQuality == PackageQuality.Release )
                {
                    if( !git.SwitchDevelopToMaster( m ) ) return false;
                    driver = driver.GetCurrentBranchDriver();
                }
                if( !git.SetVersionTag( m, targetVersion ) ) return false;
                tagCreated = true;
                if( !driver.Build( m, withUnitTest: true, withZeroBuilder: true, withPushToRemote: false ) ) return false;
                return true;

            }
            catch( Exception ex )
            {
                m.Error( "Build failed.", ex );
                return false;
            }
        }

    }
}
