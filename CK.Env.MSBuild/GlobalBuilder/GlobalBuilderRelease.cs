using CK.Core;
using CK.Text;
using CKSetup;
using CSemVer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    class GlobalBuilderRelease : GlobalBuilder
    {
        readonly SimpleRoadmap _roadmap;

        public GlobalBuilderRelease(
            SolutionDependencyResult r,
            FileSystem fileSystem,
            ILocalFeedProvider feeds,
            ITestRunMemory testRunMemory,
            GlobalBuilderInfo buildInfo,
            SimpleRoadmap roadmap )
            : base( r, fileSystem, feeds, testRunMemory, buildInfo )
        {
            if( roadmap == null ) throw new ArgumentNullException( nameof( roadmap ) );
            if( BuildInfo.WorkStatus != WorkStatus.Releasing && BuildInfo.WorkStatus != WorkStatus.CancellingRelease )
            {
                throw new InvalidOperationException( nameof( WorkStatus ) );
            }
            _roadmap = roadmap;
        }

        bool Releasing => BuildInfo.WorkStatus == WorkStatus.Releasing;

        bool CancelingRelease => BuildInfo.WorkStatus == WorkStatus.CancellingRelease;

        protected override IReadOnlyList<SolutionDependencyResult.DependentSolution> FilterSolutions( IReadOnlyList<SolutionDependencyResult.DependentSolution> solutions )
        {
            return solutions.Where( s => _roadmap.FirstOrDefault( r => r.SolutionName == s.Solution.UniqueSolutionName ).SolutionName != null ).ToList();
        }

        protected override bool StartBuilding( IActivityMonitor m, IReadOnlyList<SolutionDependencyResult.DependentSolution> solutions, SolutionDependencyResult.DependentSolution s )
        {
            if( CancelingRelease )
            {
                var v = GetTargetVersionFromRoadmap( s );
                if( v != null ) s.Solution.GitFolder.ClearVersionTag( m, v );
            }
            return base.StartBuilding( m, solutions, s );
        }

        protected override SVersion GetDependentPackageVersion( IActivityMonitor m, string packageId )
        {
            return CancelingRelease
                    ? Feeds.GetBestLocalCIVersion( m, packageId )
                    : Feeds.GetAllPackageFilesInReleaseFeed( m ).Single( p => p.PackageId == packageId ).Version;
        }

        protected override SVersion GetTargetVersion( IActivityMonitor m, SolutionDependencyResult.DependentSolution s )
        {
            if( CancelingRelease ) return base.GetTargetVersion( m, s );
            return GetTargetVersionFromRoadmap( s );
        }

        SVersion GetTargetVersionFromRoadmap( SolutionDependencyResult.DependentSolution s )
        {
            var entry = _roadmap.FirstOrDefault( r => r.SolutionName == s.Solution.UniqueSolutionName );
            return entry.Build ? entry.TargetVersion : null;
        }

        protected override bool OnBuildStart( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            if( Releasing )
            {
                if( v.Prerelease.Length == 0 )
                {
                    if( !s.Solution.GitFolder.SwitchFromDevelopToMaster( m ) ) return false;
                }

                if( !s.Solution.GitFolder.SetVersionTag( m, v ).Success ) return false;
            }
            var storePath = Path.Combine( GetTargetFeedFolderPath( m ), LocalFeedProviderExtension.CKSetupStoreName );

            if( !s.Solution.GitFolder.EnsureCKSetupStoreTestHelperConfig( m, storePath ) 
                || !s.Solution.GitFolder.EnsureLocalFeedsNuGetSource( m, ensureRelease: true, ensureCI: true ).Success 
                || !s.Solution.GitFolder.SetRepositoryXmlIgnoreDirtyFolders( m ) )
            {
                s.Solution.GitFolder.ClearVersionTag( m, v );
                // This is an untracked file. It has to be removed.
                s.Solution.GitFolder.RemoveCKSetupStoreTestHelperConfig( m );
                s.Solution.GitFolder.ResetHard( m );
                return false;
            }
            return true;
        }

        protected override bool OnBuildSucceed( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            // This is an untracked file. It has to be removed.
            s.Solution.GitFolder.RemoveCKSetupStoreTestHelperConfig( m );
            if( !s.Solution.GitFolder.ResetHard( m ) ) return false; 
            if( v.Prerelease.Length == 0 )
            {
                return s.Solution.GitFolder.SwitchFromMasterToDevelop( m );
            }
            return true;
        }

        protected override void OnBuildFailed( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            s.Solution.GitFolder.ClearVersionTag( m, v );
            // This is an untracked file. It has to be removed.
            s.Solution.GitFolder.RemoveCKSetupStoreTestHelperConfig( m );
            s.Solution.GitFolder.ResetHard( m ); 
            if( v.Prerelease.Length == 0 )
            {
                s.Solution.GitFolder.SwitchFromMasterToDevelop( m );
            }
        }

    }
}
