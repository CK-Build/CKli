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
            SolutionDependencyContext r,
            FileSystem fileSystem,
            ILocalFeedProvider feeds,
            INuGetClient nuGetClient,
            ITestRunMemory testRunMemory,
            GlobalBuilderInfo buildInfo,
            SimpleRoadmap roadmap )
            : base( r, fileSystem, feeds, nuGetClient, testRunMemory, buildInfo )
        {
            if( roadmap == null ) throw new ArgumentNullException( nameof( roadmap ) );
            if( BuildInfo.WorkStatus != GlobalWorkStatus.Releasing && BuildInfo.WorkStatus != GlobalWorkStatus.CancellingRelease )
            {
                throw new InvalidOperationException( nameof( GlobalWorkStatus ) );
            }
            _roadmap = roadmap;
        }

        bool Releasing => BuildInfo.WorkStatus == GlobalWorkStatus.Releasing;

        bool CancelingRelease => BuildInfo.WorkStatus == GlobalWorkStatus.CancellingRelease;

        protected override IReadOnlyList<SolutionDependencyContext.DependentSolution> FilterSolutions( IReadOnlyList<SolutionDependencyContext.DependentSolution> solutions )
        {
            return solutions.Where( s => _roadmap[s.Solution]?.Build ?? false ).ToList();
        }

        SimpleRoadmap.Entry _currentEntry;

        protected override bool StartBuilding( IActivityMonitor m, IReadOnlyList<SolutionDependencyContext.DependentSolution> solutions, SolutionDependencyContext.DependentSolution s )
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

        protected override SVersion GetTargetVersion( IActivityMonitor m, SolutionDependencyContext.DependentSolution s )
        {
            if( CancelingRelease ) return base.GetTargetVersion( m, s );
            return _currentEntry.ReleaseInfo.Version;
        }

        protected override bool OnBuildStart( IActivityMonitor m, SolutionDependencyContext.DependentSolution s, SVersion v )
        {
            if( Releasing )
            {
                if( v.Prerelease.Length == 0 )
                {
                    if( !s.Solution.GitFolder.SwitchDevelopToMaster( m ) ) return false;
                }

                if( !s.Solution.GitFolder.SetVersionTag( m, v ).Success ) return false;
            }
            var storePath = Path.Combine( GetTargetFeedFolderPath( m ), LocalFeedProviderExtension.CKSetupStoreName );

            var fCKSetupStore = s.Solution.GetPlugin<CK.Env.Plugins.SolutionFiles.CKSetupStoreTestHelperConfigFile>();
            bool success = fCKSetupStore.EnsureStorePath( m, storePath );

            var fNuGet = s.Solution.GetPlugin<CK.Env.Plugins.SolutionFiles.NugetConfigFile>();
            fNuGet.EnsureLocalFeeds(m, ensureCI: true, ensureRelease: true);
            success &= fNuGet.Save( m );

            var rfile = s.Solution.GetPlugin<CK.Env.Plugins.SolutionFiles.RepositoryXmlFile>();
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

        protected override bool OnBuildSucceed( IActivityMonitor m, SolutionDependencyContext.DependentSolution s, SVersion v )
        {
            _currentEntry = null;
            // This is an untracked file. It has to be removed.
            s.Solution.GetPlugin<CK.Env.Plugins.SolutionFiles.CKSetupStoreTestHelperConfigFile>().Delete( m );
            if( !s.Solution.GitFolder.ResetHard( m ) ) return false; 
            if( v.Prerelease.Length == 0 )
            {
                return s.Solution.GitFolder.SwitchMasterToDevelop( m );
            }
            return true;
        }

        protected override void OnBuildFailed( IActivityMonitor m, SolutionDependencyContext.DependentSolution s, SVersion v )
        {
            _currentEntry = null;
            s.Solution.GitFolder.ClearVersionTag( m, v );
            // This is an untracked file. It has to be removed.
            s.Solution.GetPlugin<CK.Env.Plugins.SolutionFiles.CKSetupStoreTestHelperConfigFile>().Delete( m );
            s.Solution.GitFolder.ResetHard( m ); 
            if( v.Prerelease.Length == 0 )
            {
                s.Solution.GitFolder.SwitchMasterToDevelop( m );
            }
        }

    }
}
