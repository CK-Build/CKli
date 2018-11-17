using CK.Core;
using CK.NuGetClient;
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
            INuGetClient nuGetClient,
            ITestRunMemory testRunMemory,
            GlobalBuilderInfo buildInfo,
            SimpleRoadmap roadmap )
            : base( r, fileSystem, feeds, nuGetClient, testRunMemory, buildInfo )
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
            return solutions.Where( s => _roadmap[s.Solution]?.Build ?? false ).ToList();
        }

        SimpleRoadmap.Entry _currentEntry;

        protected override bool StartBuilding( IActivityMonitor m, IReadOnlyList<SolutionDependencyResult.DependentSolution> solutions, SolutionDependencyResult.DependentSolution s )
        {
            _currentEntry = _roadmap[s.Solution];
            Debug.Assert( _currentEntry != null && _currentEntry.Build );
            if( CancelingRelease )
            {
                var v = _currentEntry.ReleaseInfo.Version;
                Debug.Assert( v != null );
                s.Solution.GitFolder.ClearVersionTag( m, v );
            }
            return base.StartBuilding( m, solutions, s );
        }

        protected override SVersion GetDependentPackageVersion( IActivityMonitor m, string packageId )
        {
            return CancelingRelease
                    ? Feeds.GetBestLocalCIVersion( m, packageId )
                    : _currentEntry.Upgrades.FirstOrDefault( u => u.PackageId == packageId ).Version;
        }

        protected override SVersion GetTargetVersion( IActivityMonitor m, SolutionDependencyResult.DependentSolution s )
        {
            if( CancelingRelease ) return base.GetTargetVersion( m, s );
            return _currentEntry.ReleaseInfo.Version;
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

            var fCKSetupStore = s.Solution.GetPlugin<SolutionFiles.CKSetupStoreTestHelperConfigFile>();
            bool success = fCKSetupStore.EnsureStorePath( m, storePath );

            var fNuGet = s.Solution.GetPlugin<SolutionFiles.NugetConfigFile>();
            fNuGet.EnsureLocalFeeds( m, ensureRelease: true, ensureCI: true );
            success &= fNuGet.Save( m );

            var rfile = s.Solution.GetPlugin<SolutionFiles.RepositoryXmlFile>();
            rfile.SetIgnoreDirtyFolders();
            success &= rfile.Save( m );

            if( !success )
            {
                s.Solution.GitFolder.ClearVersionTag( m, v );
                // This is an untracked file. It has to be removed.
                fCKSetupStore.Delete( m );
                s.Solution.GitFolder.ResetHard( m );
                return false;
            }
            return true;
        }

        protected override bool OnBuildSucceed( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            _currentEntry = null;
            // This is an untracked file. It has to be removed.
            s.Solution.GetPlugin<SolutionFiles.CKSetupStoreTestHelperConfigFile>().Delete( m );
            if( !s.Solution.GitFolder.ResetHard( m ) ) return false; 
            if( v.Prerelease.Length == 0 )
            {
                return s.Solution.GitFolder.SwitchFromMasterToDevelop( m );
            }
            return true;
        }

        protected override void OnBuildFailed( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            _currentEntry = null;
            s.Solution.GitFolder.ClearVersionTag( m, v );
            // This is an untracked file. It has to be removed.
            s.Solution.GetPlugin<SolutionFiles.CKSetupStoreTestHelperConfigFile>().Delete( m );
            s.Solution.GitFolder.ResetHard( m ); 
            if( v.Prerelease.Length == 0 )
            {
                s.Solution.GitFolder.SwitchFromMasterToDevelop( m );
            }
        }

    }
}
