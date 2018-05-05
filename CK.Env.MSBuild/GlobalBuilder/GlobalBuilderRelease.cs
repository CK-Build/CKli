using CK.Core;
using CK.Text;
using CKSetup;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    public class GlobalBuilderRelease : GlobalBuilder
    {
        readonly List<Entry> _roadMap;

        struct Entry
        {
            public readonly string SolutionName;
            public readonly SVersion TargetVersion;
            public readonly bool Build;

            public Entry( XElement e )
            {
                SolutionName = (string)e.Attribute( "Name" );
                TargetVersion = SVersion.Parse( (string)e.Attribute( "Version" ) );
                Build = e.Element( "Build" ) != null;
            }
        }

        public GlobalBuilderRelease(
            SolutionDependencyResult r,
            FileSystem fileSystem,
            ILocalFeedProvider feeds,
            ITestRunMemory testRunMemory,
            GlobalBuilderInfo buildInfo,
            XElement roadMap )
            : base( r, fileSystem, feeds, testRunMemory, buildInfo )
        {
            if( roadMap == null ) throw new ArgumentNullException( nameof( roadMap ) );
            _roadMap = roadMap.Elements( "S" )
                                .Select( e => new Entry( e ) )
                                .ToList();
        }

        protected override IReadOnlyList<SolutionDependencyResult.DependentSolution> FilterSolutions( IReadOnlyList<SolutionDependencyResult.DependentSolution> solutions )
        {
            return solutions.Where( s => _roadMap.FirstOrDefault( r => r.SolutionName == s.Solution.UniqueSolutionName ).SolutionName != null ).ToList();
        }

        protected override SVersion GetTargetVersion( IActivityMonitor m, SolutionDependencyResult.DependentSolution s )
        {
            var entry = _roadMap.FirstOrDefault( r => r.SolutionName == s.Solution.UniqueSolutionName );
            return entry.Build ? entry.TargetVersion : null;
        }

        protected override bool OnBuildStart( IActivityMonitor m, SolutionDependencyResult.DependentSolution s, SVersion v )
        {
            if( v.Prerelease.Length == 0 )
            {
                if( !s.Solution.GitFolder.SwitchFromDevelopToMaster( m ) ) return false;
            }

            if( !s.Solution.GitFolder.SetVersionTag( m, v ).Success ) return false;

            var storePath = Path.Combine( GetTargetFeedFolderPath( m ), "CKSetupStore" );

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
