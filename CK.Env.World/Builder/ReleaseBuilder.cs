using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CK.Core;
using CSemVer;

namespace CK.Env
{
    class ReleaseBuilder : Builder
    {
        readonly string[] _commits;
        readonly ReleaseRoadmap _roadmap;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        ZeroBuilder _zBuilder;

        public ReleaseBuilder(
                ArtifactCenter artifacts,
                ReleaseRoadmap roadmap,
                IEnvLocalFeedProvider localFeedProvider,
                Func<IActivityMonitor, IDependentSolutionContext, string, ISolutionDriver> driverFinder )
            : base( BuildResultType.Release, artifacts, roadmap.DependentSolutionContext, driverFinder )
        {
            _commits = new string[roadmap.DependentSolutionContext.Solutions.Count];
            _localFeedProvider = localFeedProvider;
            _roadmap = roadmap;
        }

        protected override bool OnBeforePrepareBuild( IActivityMonitor m )
        {
            _zBuilder = ZeroBuilder.EnsureZeroBuildProjects( m, _localFeedProvider, DependentSolutionContext, DriverFinder );
            return _zBuilder != null;
        }

        protected override (SVersion Version, bool MustBuild) PrepareBuild( IActivityMonitor m, IDependentSolution s, ISolutionDriver driver, IReadOnlyList<UpdatePackageInfo> upgrades )
        {
            Debug.Assert( driver.GitRepository.CurrentBranchName == s.BranchName );

            IReleaseSolutionInfo info = _roadmap.ReleaseInfos[ s.Index ];
            var targetVersion = info.CurrentReleaseInfo.Version;
            if( info.CurrentReleaseInfo.Level != ReleaseLevel.None )
            {
                if( !driver.UpdatePackageDependencies( m, upgrades ) ) return (null,false);
                if( !driver.GitRepository.Commit( m, "Global Release commit." ) ) return (null,false);
            }
            _commits[s.Index] = driver.GitRepository.Head.CommitSha;
            return (targetVersion, info.CurrentReleaseInfo.Level != ReleaseLevel.None);
        }

        protected override IReadOnlyList<ReleaseNoteInfo> GetReleaseNotes() => _roadmap.GetReleaseNotes();

        protected override bool OnBeforeBuild( IActivityMonitor m, BuildResult result )
        {
            _zBuilder.RegisterSHAlias( m );
            return true;
        }

        protected override bool Build(
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
                m.Error( $"Commit changed between PrepareBuild call and this Build. Build canceled." );
                return false;
            }
            bool buildResult = DoBuild( m, driver, buildProjectsUpgrade, targetVersion, sVersion != null );
            if( sVersion != null )
            {
                if( !buildResult )
                {
                    driver.GitRepository.ClearVersionTag( m, targetVersion );
                }
                if( targetVersion.PackageQuality == PackageQuality.Release )
                {
                    buildResult &= driver.GitRepository.SwitchMasterToDevelop( m );
                }
            }
            return buildResult;
        }

        private static bool DoBuild(
            IActivityMonitor m,
            ISolutionDriver driver,
            IEnumerable<UpdatePackageInfo> buildProjectsUpgrade,
            CSVersion targetVersion,
            bool doBuild )
        {
            try
            {
                if( !driver.UpdatePackageDependencies( m, buildProjectsUpgrade ) ) return false;
                var git = driver.GitRepository;
                if( !doBuild )
                {
                    // No release must be done but build project dependencies have been upgraded.
                    return git.Commit( m, "Upgraded build dependencies." );
                }

                if( !git.AmendCommit( m ) ) return false;
                if( targetVersion.PackageQuality == PackageQuality.Release )
                {
                    if( !git.SwitchDevelopToMaster( m ) ) return false;
                    driver = driver.GetCurrentBranchDriver();
                }
                if( !git.SetVersionTag( m, targetVersion ) ) return false;
                if( !driver.Build( m, withUnitTest: true, withZeroBuilder: true, withPushToRemote: false ) ) return false;
                return true;

            }
            catch( Exception ex )
            {
                m.Error( "Build failed.", ex );
                return false;
            }
        }

        protected override BuildResult OnBuildSuccess( IActivityMonitor m, BuildResult result )
        {
            _zBuilder.RegisterSHAlias( m );
            return result;
        }

    }
}
