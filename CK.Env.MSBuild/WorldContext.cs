using CK.Core;
using CK.Text;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using SimpleGitVersion;
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
    public class WorldContext
    {
        readonly GlobalGitContext _globalGitContext;
        readonly IWorldStore _worldStore;
        readonly ILocalFeedProvider _feeds;
        readonly IFileProvider _referential;
        readonly Func<IActivityMonitor, string, IReadOnlyList<Solution>> _branchSolutionsLoader;
        readonly WorldState _worldState;
        readonly TestedCommitMemory _testedCommits;
        readonly GlobalBuilderInfo _buildInfo;

        class TestedCommitMemory : ITestRunMemory
        {
            readonly WorldContext _ctx;
            readonly HashSet<string> _testedCommitMemory;

            public TestedCommitMemory( WorldContext ctx )
            {
                _ctx = ctx;
                _testedCommitMemory = new HashSet<string>();
                string[] id = _ctx._worldState.XmlState.Element( "TestedCommitMemory" )?.Value.Split( '|' );
                if( id != null ) _testedCommitMemory.AddRangeArray( id );
                else _ctx._worldState.XmlState.Add( new XElement( "TestedCommitMemory" ) );
            }

            bool UpdateXmlState( IActivityMonitor m )
            {
                if( HasChanged )
                {
                    _ctx.SetState( m, state => state.XmlState.Element( "TestedCommitMemory" ).Value = _testedCommitMemory.Concatenate( "|" ) );
                    HasChanged = false;
                    return true;
                }
                return false;
            }

            public bool HasChanged { get; private set; }

            public bool HasBeenTested( IActivityMonitor m, string key )
            {
                return _testedCommitMemory.Contains( key );
            }

            public void SetTested( IActivityMonitor m, string key )
            {
                HasChanged |= _testedCommitMemory.Add( key );
                UpdateXmlState( m );
            }
        }

        WorldContext(
            GlobalGitContext git,
            IWorldStore store,
            ILocalFeedProvider feeds,
            IFileProvider referential,
            WorldState worldState,
            Func<IActivityMonitor, string, IReadOnlyList<Solution>> branchSolutionsLoader )
        {
            _globalGitContext = git;
            _worldStore = store;
            _feeds = feeds;
            _referential = referential;
            _worldState = worldState;
            _branchSolutionsLoader = branchSolutionsLoader;
            _testedCommits = new TestedCommitMemory( this );
            _buildInfo = new GlobalBuilderInfo();
        }

        bool SetState( IActivityMonitor m, Action<WorldState> a ) => _worldStore.SetState( m, _worldState, a );

        public IWorldName World => _globalGitContext.World;

        public FileSystem FileSystem => _globalGitContext.FileSystem;

        public GlobalGitStatus GlobalGitStatus => _worldState.GlobalGitStatus;

        /// <summary>
        /// Gets the current, stable, branch name. This is null when <see cref="IsTransitioning"/> is true.
        /// </summary>
        public string CurrentBranchName => _worldState.GlobalGitStatus == GlobalGitStatus.DevelopBranch
                                                ? World.DevelopBranchName
                                                : (_worldState.GlobalGitStatus == GlobalGitStatus.LocalBranch
                                                      ? World.LocalBranchName
                                                      : null);

        public bool IsTransitioning => GlobalGitStatus == GlobalGitStatus.FromDevelopToLocal || GlobalGitStatus == GlobalGitStatus.FromLocalToDevelop;

        public bool HasWorkPending => GlobalGitStatus == GlobalGitStatus.Releasing || IsTransitioning;

        public bool ConcludeCurrentWork( IActivityMonitor m )
        {
            if( _worldState.GlobalGitStatus == GlobalGitStatus.FromLocalToDevelop )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromLocalToDevelop( m ) ) return false;
                    // Coming from local, build needs to access the local feeds.
                    // This should be the case but this corrects the files if needed.
                    g.EnsureLocalFeedsNuGetSource( m );
                }
                var r = GetSolutionDependencyResult( m, World.DevelopBranchName );
                if( r == null ) return false;
                _buildInfo.SetGlobalGitStatus( GlobalGitStatus.FromLocalToDevelop );
                var b = new GlobalBuilder( r, FileSystem, _feeds, _testedCommits, _buildInfo );
                if( !b.Build( m ) ) return false;
                using( m.OpenInfo( $"Upgrading Build projects dependencies and removing LocalFeed NuGet sources." ) )
                {
                    foreach( var s in r.Solutions )
                    {
                        if( !s.UpgradePackagesToTheMax( m, _feeds, FileSystem, allowDowngrade: false, buildProjects: true ) ) return false;
                        if( !s.Solution.GitFolder.RemoveLocalFeedsNuGetSource( m ).Success ) return false;
                        if( !s.Solution.GitFolder.Commit( m, "Updated Build projects and removed LocalFeed NuGet sources." ).Success ) return false;
                    }
                }
                if( !SetState( m, state => state.GlobalGitStatus = GlobalGitStatus.DevelopBranch ) ) return false;
            }
            if( _worldState.GlobalGitStatus == GlobalGitStatus.FromDevelopToLocal )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromDevelopToLocal( m ) ) return false;
                }
                var r = GetSolutionDependencyResult( m, World.LocalBranchName );
                if( r == null ) return false;
                _buildInfo.SetGlobalGitStatus( GlobalGitStatus.FromDevelopToLocal );
                var b = new GlobalBuilder( r, FileSystem, _feeds, _testedCommits, _buildInfo );
                if( !b.Build( m ) ) return false;
                if( !SetState( m, state => state.GlobalGitStatus = GlobalGitStatus.LocalBranch ) ) return false;
            }
            if( _worldState.GlobalGitStatus == GlobalGitStatus.Releasing )
            {
                foreach( var g in _globalGitContext.GitFolders )
                {
                    if( !g.SwitchFromMasterToDevelop( m ) ) return false;
                }
                var r = GetSolutionDependencyResult( m, World.DevelopBranchName );
                if( r == null ) return false;
                var roadMap = _worldState.XmlState.Element( "RoadMap" );
                if( roadMap == null )
                {
                    m.Error( "Unable to find the RoadMap element in WorldState XmlState." );
                    return false;
                }
                if( !DoRelease( m, r, roadMap ) ) return false;
            }
            m.Info( $"Work done. Current Global Status: {_worldState.GlobalGitStatus}." );
            return true;
        }

        bool DoRelease( IActivityMonitor m, SolutionDependencyResult r, XElement roadMap )
        {
            _buildInfo.SetGlobalGitStatus( GlobalGitStatus.Releasing );
            var b = new GlobalBuilderRelease( r, FileSystem, _feeds, _testedCommits, _buildInfo, roadMap );
            if( !b.Build( m ) ) return false;
            return SetState( m, state => { roadMap.Remove(); state.GlobalGitStatus = GlobalGitStatus.DevelopBranch; } );
        }

        public bool CanSwitchToDevelop => GlobalGitStatus == GlobalGitStatus.LocalBranch;

        public bool SwitchToDevelop( IActivityMonitor m )
        {
            if( GlobalGitStatus != GlobalGitStatus.LocalBranch ) throw new InvalidOperationException();
            return SetState( m, state => state.GlobalGitStatus = GlobalGitStatus.FromLocalToDevelop )
                   && ConcludeCurrentWork( m );
        }

        public bool CanSwitchToLocal => GlobalGitStatus == GlobalGitStatus.DevelopBranch;

        public bool SwitchToLocal( IActivityMonitor m )
        {
            if( GlobalGitStatus != GlobalGitStatus.DevelopBranch ) throw new InvalidOperationException();
            return SetState( m, state => state.GlobalGitStatus = GlobalGitStatus.FromDevelopToLocal )
                   && ConcludeCurrentWork( m );
        }

        public bool CanRunCIBuild => GlobalGitStatus == GlobalGitStatus.DevelopBranch || GlobalGitStatus == GlobalGitStatus.LocalBranch;

        /// <summary>
        /// Runs a CI build.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool RunCIBuild( IActivityMonitor m )
        {
            if( !CanRunCIBuild ) throw new InvalidOperationException();
            _buildInfo.SetGlobalGitStatus( _worldState.GlobalGitStatus );
            var r = GetSolutionDependencyResult( m, GlobalGitStatus == GlobalGitStatus.LocalBranch
                                                        ? World.LocalBranchName
                                                        : World.DevelopBranchName );
            if( r == null ) return false;
            var b = new GlobalBuilder( r, FileSystem, _feeds, _testedCommits, _buildInfo );
            return b.Build( m );
        }

        public bool CanLocalFixToZeroBuildProjects => GlobalGitStatus == GlobalGitStatus.LocalBranch;

        public bool LocalFixToZeroBuildProjects( IActivityMonitor m, bool commitChanges = false )
        {
            if( !CanLocalFixToZeroBuildProjects ) throw new InvalidOperationException();
            IReadOnlyList<Solution> solutions = _branchSolutionsLoader( m, World.LocalBranchName );
            if( solutions.Any( s => s == null ) ) return false;

            var r = GetSolutionDependencyResult( m, GlobalGitStatus == GlobalGitStatus.LocalBranch
                                                        ? World.LocalBranchName
                                                        : World.DevelopBranchName );
            if( r == null ) return false;

            void AddBuidDeps( Project p, HashSet<Project> collector )
            {
                if( collector.Add( p ) )
                {
                    foreach( var d in r.ProjectDependencies.DependencyTable
                                                            .Where( row => row.SourceProject.Project == p && row.TargetPackage.Project != null )
                                                            .Select( row => row.TargetPackage.Project ) )
                    {
                        AddBuidDeps( d, collector );
                    }
                    foreach( var inSolution in p.Deps.Projects.Select( p2p => p2p.TargetProject ) )
                    {
                        AddBuidDeps( inSolution, collector );
                    }
                }
            }

            var buildDeps = new HashSet<Project>();
            foreach( var p in r.ProjectDependencies.DependencyTable
                                                    .Where( row => row.SourceProject.Project.IsBuildProject && row.TargetPackage.Project != null )
                                                    .Select( row => row.TargetPackage.Project ) )
            {
                AddBuidDeps( p, buildDeps );
            }
            var buildDepNames = new HashSet<string>( buildDeps.Select( p => p.Name ) );

            using( m.OpenInfo( $"Removing {buildDepNames.Concatenate()} packages ZeroVersion from local NuGet cache." ) )
            {
                foreach( var n in buildDepNames )
                {
                    _feeds.RemoveFromNuGetCache( m, n, SVersion.ZeroVersion );
                }
            }
            foreach( var s in r.Solutions )
            {
                var projects = buildDeps.Where( d => d.PrimarySolution == s.Solution ).ToList();
                m.Info( $"Solution {s.Solution.UniqueSolutionName} has {s.Solution.BuildProjects.Count} build project(s)." );
                foreach( var p in projects.Concat( s.Solution.BuildProjects ) )
                {
                    foreach( var dep in p.Deps.Packages.Where( rawDep => buildDepNames.Contains( rawDep.PackageId ) ) )
                    {
                        p.SetPackageReferenceVersion( m, p.TargetFrameworks, dep.PackageId, SVersion.ZeroVersion );
                    }
                }
                if( !s.Solution.Save( m, FileSystem ) ) return false;
                if( projects.Count > 0 )
                {
                    using( m.OpenInfo( $"Generating ZeroVersion in Local Blank Feed for packages: {projects.Select( p => p.Name ).Concatenate()}" ) )
                    {
                        var args = $@"pack --output ""{_feeds.GetLocalFeedFolder( m ).PhysicalPath}"" --include-symbols --configuration Debug /p:Version=""{SVersion.ZeroVersion}"" /p:AssemblyVersion=""{InformationalVersion.ZeroAssemblyVersion}"" /p:FileVersion=""{InformationalVersion.ZeroFileVersion}"" /p:InformationalVersion=""{InformationalVersion.ZeroInformationalVersion}"" ";
                        foreach( var p in projects )
                        {
                            var path = FileSystem.GetFileInfo( p.Path.RemoveLastPart() ).PhysicalPath;
                            FileSystem.RawDeleteLocalDirectory( m, Path.Combine( path, "bin" ) );
                            FileSystem.RawDeleteLocalDirectory( m, Path.Combine( path, "obj" ) );
                        }
                        // Quick and dirty fix: ordering by project references count may avoid here the need
                        // to sort projects according to their project to project depednecies which should be done...
                        foreach( var p in projects.OrderBy( p => p.Deps.Projects.Count ) )
                        {
                            var path = FileSystem.GetFileInfo( p.Path.RemoveLastPart() ).PhysicalPath;
                            if( !GlobalBuilder.Run( m, path, "dotnet", args ) ) return false;
                        }
                    }
                }
                using( m.OpenInfo( $"Updating standard CodeCakeBuilder files." ) )
                {
                    var codeCakeBuilderPath = s.Solution.SolutionFolderPath.AppendPart( "CodeCakeBuilder" );
                    foreach( var name in new[]
                    {
                        "Build.StandardCheckRepository.cs",
                        "Build.StandardCreateNuGetPackages.cs",
                        "Build.StandardPushNuGetPackages.cs",
                        "Build.StandardSolutionBuild.cs",
                        "Build.StandardUnitTests.cs"
                    } )
                    {
                        var path = codeCakeBuilderPath.AppendPart( name );
                        var f = FileSystem.GetFileInfo( path );
                        if( f.Exists )
                        {
                            var source = _referential.GetFileInfo( "InitialCodeCakeBuilder/" + name );
                            FileSystem.CopyTo( m, source, path );
                        }
                    }
                }
                if( commitChanges && !s.Solution.GitFolder.Commit( m, $"Build project now uses ZeroVersion available dependencies in Local Blank Feed." ).Success )
                {
                    return false;
                }
            }
            return true;
        }

        public bool CanRelease => GlobalGitStatus == GlobalGitStatus.DevelopBranch;

        /// <summary>
        /// Starts a release after a <see cref="GitFolder.CheckoutAndPull"/> on <see cref="IWorldName.DevelopBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="pull">Pull 'develop' branch first.</param>
        /// <returns>The global releaser or null on error.</returns>
        public bool Release( IActivityMonitor m, IReleaseVersionSelector versionSelector, bool pull = true )
        {
            if( !CanRelease ) throw new InvalidOperationException();
            var gitInfos = new Dictionary<GitFolder, RepositoryInfo>();
            foreach( var g in _globalGitContext.GitFolders )
            {
                if( !g.CheckCleanCommit( m ) ) return false;
                if( pull )
                {
                    if( !g.CheckoutAndPull( m, World.DevelopBranchName, true ).Success ) return false;
                }
                var info = g.ReadVersionInfo( m, World.DevelopBranchName );
                if( info == null ) return false;
                gitInfos.Add( g, info );
            }
            var r = GetSolutionDependencyResult( m, World.DevelopBranchName );
            if( r == null ) return false;
            var releasers = r.Solutions.Select( s => new SolutionReleaser( s, gitInfos[s.Solution.GitFolder] ) ).ToList();
            var releaser = new GlobalReleaser( releasers );
            XElement roadMap = releaser.ComputeFullRoadMap( m, versionSelector );
            if( roadMap == null ) return false;
            if( !SetState( m, state =>
            {
                state.GlobalGitStatus = GlobalGitStatus.Releasing;
                state.XmlState.Add( roadMap );
            } ) )
            {
                return false;
            }
            return DoRelease( m, r, roadMap );
        }

        SolutionDependencyResult GetSolutionDependencyResult( IActivityMonitor m, string branchName )
        {
            IReadOnlyList<Solution> solutions = _branchSolutionsLoader( m, branchName );
            if( solutions.Any( s => s == null ) ) return null;

            var deps = DependencyContext.Create( m, solutions );
            if( deps == null ) return null;
            SolutionDependencyResult r = deps.AnalyzeDependencies( m, SolutionSortStrategy.EverythingExceptBuildProjects );
            if( r.HasError )
            {
                r.RawSorterResult.LogError( m );
                return null;
            }
            return r;
        }

        public static WorldContext Create(
            IActivityMonitor m,
            IWorldName world,
            IWorldStore worldStore,
            ILocalFeedProvider feeds,
            IFileProvider referential,
            IEnumerable<GitFolder> gitFolders,
            Func<IActivityMonitor, string, IReadOnlyList<Solution>> branchSolutionsLoader )
        {
            var gitContext = new GlobalGitContext( world, gitFolders );
            var worldState = worldStore.GetLocalState( m, world );
            var recordedStatus = worldState.GlobalGitStatus;
            if( !gitContext.CheckStatus( m, ref recordedStatus ) ) return null;
            Debug.Assert( recordedStatus != GlobalGitStatus.Unknwon );
            worldState.GlobalGitStatus = recordedStatus;
            return new WorldContext( gitContext, worldStore, feeds, referential, worldState, branchSolutionsLoader );
        }

    }
}
