using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.Diff;
using CK.Env.MSBuildSln;
using CK.Text;
using CSemVer;
using CK.Env;

namespace CK.Env.Plugin
{
    public class SolutionDriver : GitBranchPluginBase, ISolutionDriver, IDisposable, ICommandMethodsProvider
    {
        public static readonly ArtifactType NuGetType = NuGet.NuGetClient.NuGetType;
        public static readonly ArtifactType CKSetupType = ArtifactType.Register( "CKSetup", false );
        /// <summary>
        /// This is shared here: more than one plugin needs this.
        /// </summary>
        public const string CODECAKEBUILDER_SECRET_KEY = "CODECAKEBUILDER_SECRET_KEY";


        readonly SecretKeyStore _keyStore;
        readonly ISolutionDriverWorld _world;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly ArtifactCenter _artifactCenter;
        readonly SolutionSpec _solutionSpec;
        readonly SolutionContext _solutionContext;

        Solution _solution;
        SolutionFile _sln;
        IReadOnlyList<(string SecretKeyName, string Secret)> _buildSecrets;
        bool _isSolutionValid;

        public SolutionDriver(
                SecretKeyStore keyStore,
                ISolutionDriverWorld w,
                GitFolder f,
                ArtifactCenter artifactCenter,
                NormalizedPath branchPath,
                SolutionSpec spec,
                IEnvLocalFeedProvider localFeedProvider )
            : base( f, branchPath )
        {
            _solutionContext = w.Register( this );
            _world = w;
            _artifactCenter = artifactCenter;
            _keyStore = keyStore;
            _solutionSpec = spec;
            _localFeedProvider = localFeedProvider;
        }

        void IDisposable.Dispose()
        {
            _world.Unregister( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( "SolutionDriver" );

        IGitRepository ISolutionDriver.GitRepository => GitFolder;

        string ISolutionDriver.BranchName => BranchPath.LastPart;

        /// <summary>
        /// Gets whether this plugin is able to work.
        /// It provides services only on local or develop and if the <see cref="GitFolder.StandardGitStatus"/>
        /// is the same as <see cref="GitBranchPluginBase.PluginBranch"/>.
        /// </summary>
        bool IsActive => GitFolder.StandardGitStatus == PluginBranch
                         && (PluginBranch == StandardGitStatus.Local || PluginBranch == StandardGitStatus.Develop);

        /// <summary>
        /// Gets the solution driver of the <see cref="IGitRepository.CurrentBranchName"/>.
        /// </summary>
        /// <returns>This solution driver or the one of the current branch.</returns>
        public ISolutionDriver GetCurrentBranchDriver()
        {
            return GitFolder.StandardGitStatus == PluginBranch
                ? this
                : GitFolder.PluginManager.BranchPlugins[GitFolder.CurrentBranchName].GetPlugin<SolutionDriver>();
        }

        /// <summary>
        /// Fires whenever a solution has been loaded so that any other
        /// plugins can participate to its configuration.
        /// </summary>
        public event EventHandler<SolutionConfigurationEventArgs> OnSolutionConfiguration;


        /// <summary>
        /// Gets whether the solution has been correctly read and configured.
        /// Nothing should be done with the solution when this is false, except fix operations.
        /// </summary>
        public bool IsSolutionValid => _isSolutionValid;

        /// <summary>
        /// Gets the secrets required to build the solution.
        /// This not null as soon as the solution has been successfuly read (this is available even
        /// if <see cref="IsSolutionValid"/> is false).
        /// </summary>
        public IReadOnlyList<(string SecretKeyName, string Secret)> BuildRequiredSecrets => _buildSecrets;

        /// <summary>
        /// Forces the solution to be reloaded next time <see cref="GetSolution"/> is called.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void SetSolutionDirty( IActivityMonitor m )
        {
            if( _sln != null )
            {
                _sln.Saved -= OnSolutionSaved;
                m.Info( $"Solution '{GitFolder.SubPath}' must be reloaded." );
                _sln = null;
            }
        }

        /// <summary>
        /// Gets the Solution that this driver handles.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="reloadSolution">True to force a lod (like having called <see cref="SetSolutionDirty(IActivityMonitor)"/>).</param>
        /// <param name="allowInvalidSolution">
        /// True to allow <see cref="IsSolutionValid"/> to be false: the instance is returned as long as the <see cref="ISolution"/> instance has
        /// successfully been built even if some required cheks have failed.
        /// </param>
        /// <returns>The solution or null if unable to load the solution.</returns>
        public ISolution GetSolution( IActivityMonitor monitor, bool allowInvalidSolution, bool reloadSolution = false )
        {
            if( !_isSolutionValid || reloadSolution )
            {
                using( monitor.OpenInfo( $"Loading solution '{GitFolder.SubPath}'." ) )
                {
                    DoLoadSolution( monitor );
                }
            }
            return _isSolutionValid || allowInvalidSolution ? _solution : null;
        }

        void DoLoadSolution( IActivityMonitor m )
        {
            _isSolutionValid = false;
            var expectedSolutionName = GitFolder.SubPath.LastPart + ".sln";
            _buildSecrets = null;
            _sln = SolutionFile.Read( GitFolder.FileSystem, m, BranchPath.AppendPart( expectedSolutionName ) );
            if( _sln == null ) return;

            _sln.Saved += OnSolutionSaved;
            bool newSolution = false;
            if( _solution == null )
            {
                newSolution = true;
                _solution = _solutionContext.AddSolution( BranchPath, expectedSolutionName );
                foreach( var targetName in _solutionSpec.ArtifactTargets )
                {
                    var r = _artifactCenter.Repositories.FirstOrDefault( repo => repo.UniqueRepositoryName == targetName );
                    if( r == null ) m.Error( $"Unable to find the repository named '{targetName}' (available repositories: {_artifactCenter.Repositories.Select( repo => repo.UniqueRepositoryName ).Concatenate()})." );
                    else _solution.AddArtifactTarget( r );
                }
                foreach( var sourceName in _solutionSpec.ArtifactSources )
                {
                    var f = _artifactCenter.Feeds.FirstOrDefault( feed => feed.TypedName == sourceName );
                    if( f == null ) m.Error( $"Unable to find the feed named '{sourceName}' (available sources: {_artifactCenter.Feeds.Select( feed => feed.TypedName ).Concatenate()})." );
                    _solution.AddArtifactSource( f );
                }
            }
            _solution.Tag( _sln );
            var projectsToRemove = new HashSet<DependencyModel.Project>( _solution.Projects );
            var orderedProjects = new DependencyModel.Project[_sln.MSProjects.Count];
            int i = 0;
            bool badPack = false;
            foreach( var p in _sln.MSProjects )
            {
                if( p.ProjectName != p.SolutionRelativeFolderPath.LastPart )
                {
                    m.Warn( $"Project named {p.ProjectName} should be in folder of the same name, not in {p.SolutionRelativeFolderPath.LastPart}." );
                }
                Debug.Assert( p.ProjectFile != null );

                bool doPack = p.IsPackable ?? true;
                if( doPack == true )
                {
                    if( _solutionSpec.NotPublishedProjects.Contains( p.Path ) )
                    {
                        m.Error( $"Project {p.ProjectName} that must not be published have the Element IsPackable not set to false." );
                        badPack = true;
                    }
                    else if( !_solutionSpec.TestProjectsArePublished && (p.Path.Parts.Contains( "Tests" ) || p.ProjectName.EndsWith( ".Tests" )) )
                    {
                        m.Error( $"Tests Project {p.ProjectName} does not have IsPackable set to false." );
                        badPack = true;
                    }
                    else if( p.ProjectName.Equals( "CodeCakeBuilder", StringComparison.OrdinalIgnoreCase ) )
                    {
                        m.Error( $"CodeCakeBuilder Project does not have IsPackable set to false." );
                        badPack = true;
                    }
                    else
                    {
                        m.Info( $"Project {p.ProjectName} will be publised." );
                    }
                }
                else
                {
                    m.Info( $"Project {p.ProjectName} is set to not be packed: this project won't be published." );
                }
                var (project, isNewProject) = _solution.AddOrFindProject( p.SolutionRelativeFolderPath, ".Net", p.ProjectName, p.TargetFrameworks );
                project.Tag( p );
                if( isNewProject )
                {
                    ConfigureFromSpec( m, project, _solutionSpec );
                }
                SynchronizePackageReferences( m, project );
                projectsToRemove.Remove( project );
                orderedProjects[i++] = project;
            }
            foreach( var project in _solution.Projects.Where( p => p.Tag<MSProject>() != null ) )
            {
                SynchronizeProjectReferences( m, project, msProj => orderedProjects[msProj.MSProjIndex] );
            }
            foreach( var noMore in projectsToRemove ) _solution.RemoveProject( noMore );

            List<(string SecretKeyName, string Secret)> buildSecrets = _artifactCenter.ResolveSecrets( m, _solution.ArtifactTargets, false );
            _isSolutionValid = !badPack;
            var h = OnSolutionConfiguration;
            if( h != null )
            {
                var e = new SolutionConfigurationEventArgs( m, _solution, newSolution, _solutionSpec, buildSecrets );
                h( this, e );
                if( e.ConfigurationFailed )
                {
                    m.Error( "Solution initialization failed: " + e.FailureMessage );
                    _isSolutionValid = false;
                }
            }
            _buildSecrets = buildSecrets;
        }

        void OnSolutionSaved( object sender, EventMonitoredArgs e )
        {
            SetSolutionDirty( e.Monitor );
        }

        static void ConfigureFromSpec( IActivityMonitor m, DependencyModel.Project project, SolutionSpec spec )
        {
            var msProject = project.Tag<MSProject>();
            if( project.SimpleProjectName == "CodeCakeBuilder" )
            {
                project.IsBuildProject = true;
            }
            else
            {
                project.IsTestProject = project.SimpleProjectName.EndsWith( ".Tests" );
                bool mustPublish;
                if( spec.PublishedProjects.Count > 0 )
                {
                    mustPublish = spec.PublishedProjects.Contains( project.SolutionRelativeFolderPath );
                }
                else
                {
                    bool notPublished = !spec.NotPublishedProjects.Contains( project.SolutionRelativeFolderPath );
                    bool notRootDirectory = project.SolutionRelativeFolderPath.Parts.Count == 1;
                    


                    bool ignoreNotRoot = spec.PublishProjectInDirectories && !project.IsTestProject;
                    // We bindly follow the <IsPackable> element. Only if it's not defined (ie. it's null) we must "think".
                    mustPublish = msProject.IsPackable 
                                    ?? (notPublished &&
                                            (notRootDirectory || ignoreNotRoot || (project.IsTestProject && spec.TestProjectsArePublished)));
                }
                if( mustPublish )
                {
                    project.AddGeneratedArtifacts( new Artifact( NuGetType, project.SimpleProjectName ) );
                }
                if( spec.CKSetupComponentProjects.Contains( project.SimpleProjectName ) )
                {
                    if( !mustPublish )
                    {
                        m.Error( $"Project {project} cannot be a CKSetupComponent since it is not published." );
                    }
                    foreach( var name in project.Tag<MSProject>()
                                                .TargetFrameworks.AtomicTraits
                                                .Select( t => new Artifact( CKSetupType, project.SimpleProjectName + '/' + t.ToString() ) ) )
                    {
                        project.AddGeneratedArtifacts( name );
                    }
                }
            }
        }

        static void SynchronizePackageReferences( IActivityMonitor m, DependencyModel.Project project )
        {
            var toRemove = new HashSet<Artifact>( project.PackageReferences.Select( r => r.Target.Artifact ) );
            var p = project.Tag<MSProject>();
            foreach( var dep in p.Deps.Packages )
            {
                toRemove.Remove( dep.Package.Artifact );
                project.EnsurePackageReference( dep.Package, ArtifactDependencyKind.Transitive, dep.Frameworks );
            }
            foreach( var noMore in toRemove ) project.RemovePackageReference( noMore );
        }

        static void SynchronizeProjectReferences( IActivityMonitor m, DependencyModel.Project project, Func<MSProject, DependencyModel.Project> depsFinder )
        {
            var p = project.Tag<MSProject>();
            var toRemove = new HashSet<IProject>( project.ProjectReferences.Select( r => r.Target ) );
            foreach( var dep in p.Deps.Projects )
            {
                if( dep.TargetProject is MSProject target )
                {
                    var mapped = depsFinder( target );
                    Debug.Assert( mapped != null );
                    project.EnsureProjectReference( mapped, ArtifactDependencyKind.Transitive );
                    toRemove.Remove( mapped );
                }
                else
                {
                    m.Warn( $"Project '{p}' references project {dep.TargetProject} of unhandled type. Reference is ignored." );
                }
            }
            foreach( var noMore in toRemove ) project.RemoveProjectReference( noMore );
        }

        /// <summary>
        /// Gets whether <see cref="IWorldState.WorkStatus"/> is <see cref="GlobalWorkStatus.Idle"/>
        /// and this plugin is on the active branch (<see cref="IsActive"/> is true).
        /// </summary>
        public bool CanPull => _world.WorkStatus == GlobalWorkStatus.Idle && IsActive;

        /// <summary>
        /// Pulls the current branch and reloads the solutions if needed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Pull( IActivityMonitor m )
        {
            var (Success, ReloadNeeded) = GitFolder.Pull( m );
            if( !Success ) return false;
            return !ReloadNeeded || GetSolution( m, true ) != null;
        }

        [CommandMethod]
        public bool DumpLogsBetweenDates( IActivityMonitor m, string beginning, string ending )
        {
            var solution = GetSolution( m, allowInvalidSolution: true );
            if( solution == null ) return false;
            if( !DateTimeOffset.TryParse( beginning, out DateTimeOffset beginningDate ) )
            {
                m.Error( $"{beginning} is not a valid date" );
                return false;
            }
            if( !DateTimeOffset.TryParse( ending, out DateTimeOffset endingDate ) )
            {
                m.Error( $"{ending} is not a valid date" );
                return false;
            }
            m.Info( $"Parsed date range: {beginningDate} => {endingDate}" );
            GitFolder.ShowLogsBetweenDates( m, beginningDate, endingDate, solution.Projects.Select( proj => new DiffRoot( solution.Name, proj.ProjectSources ) ) );
            return true;
        }

        [CommandMethod]
        public void ShowSolutionExternalDependencies( IActivityMonitor m )
        {
            var packages = _solutionContext.GetDependencyAnalyser( m, m.ActualFilter == LogFilter.Debug ).ExternalReferences;
            if( packages.Count == 0 )
            {
                Console.WriteLine( "This Solution don't have any external references." );
            }

            Console.WriteLine( $"External dependency of the Solution {GetSolution( m, allowInvalidSolution: true ).Name}:" );
            foreach( PackageReference externalRef in packages )
            {
                Console.WriteLine( "====|" + externalRef.ToString() );
            }
        }

        /// <summary>
        /// Fires whenever a package reference version must be upgraded.
        /// </summary>
        public event EventHandler<UpdatePackageDependencyEventArgs> OnUpdatePackageDependency;

        /// <summary>
        /// Updates projects dependencies and saves the solution and its updated projects.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageInfos">The packages to update.</param>
        /// <returns>True on success, false on error.</returns>
        public bool UpdatePackageDependencies( IActivityMonitor monitor, IReadOnlyCollection<UpdatePackageInfo> packageInfos )
        {
            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;
            Debug.Assert( packageInfos.All( p => p.Project.Solution == solution ) );
            bool mustSave = false;
            foreach( var update in packageInfos )
            {
                var p = update.Project.Tag<MSProject>();
                if( p != null )
                {
                    int changes = p.SetPackageReferenceVersion( monitor, p.TargetFrameworks, update.PackageUpdate.Artifact.Name, update.PackageUpdate.Version );
                    mustSave |= changes != 0;
                }
            }
            bool error = false;
            using( monitor.OnError( () => error = true ) )
            {
                try
                {
                    var e = new UpdatePackageDependencyEventArgs( monitor, packageInfos );
                    OnUpdatePackageDependency?.Invoke( this, e );
                }
                catch( Exception ex )
                {
                    monitor.Error( "While updating dependencies.", ex );
                }
            }
            if( error ) return false;
            return mustSave ? _sln.Save( monitor ) : true;
        }


        /// <summary>
        /// Fires before and after <see cref="ZeroBuildProject"/> actually builds a
        /// project in ZeroVersion.
        /// </summary>
        public event EventHandler<ZeroBuildEventArgs> OnZeroBuildProject;

        /// <summary>
        /// Builds the given project (that must be handled by this driver otherwise an exception is thrown).
        /// This uses "dotnet pack" or "dotnet publish" depending on <see cref="ZeroBuildProjectInfo.MustPack"/>.
        /// No package updates are done by this method. Project is build as it is on the file system.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="info">The <see cref="ZeroBuildProjectInfo"/>.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ZeroBuildProject( IActivityMonitor monitor, ZeroBuildProjectInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );

            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;

            // This is required, otherwise the NuGet cache keeps the previous version (lost 4 hours of my life here).
            if( info.MustPack )
            {
                var packageName = info.Project.GeneratedArtifacts.Single( a => a.Artifact.Type == NuGetType ).Artifact.Name;
                _localFeedProvider.RemoveFromNuGetCache( monitor, packageName, SVersion.ZeroVersion );
            }
            var msP = info.Project.Tag<MSProject>();
            var msUpgrade = info.UpgradeZeroProjects.Select( p => p.Tag<MSProject>() ).ToList();
            var p2pRefToRemove = msP.Deps.Projects.Where( p2p => msUpgrade.Contains( p2p.TargetProject ) ).ToList();
            if( p2pRefToRemove.Count > 0 )
            {
                monitor.Info( $"Removing Project references: {p2pRefToRemove.Select( p2p => p2p.Element.ToString() ).Concatenate()}" );
                p2pRefToRemove.Select( p2p => p2p.Element ).Remove();
            }
            foreach( var z in msUpgrade )
            {
                msP.SetPackageReferenceVersion( monitor, msP.TargetFrameworks, z.ProjectName, SVersion.ZeroVersion, true, false, false );
            }
            if( !msP.Solution.Save( monitor ) ) return false;

            ICommitAssemblyBuildInfo b = CommitAssemblyBuildInfo.ZeroBuildInfo;
            string commonArgs = $@" --no-dependencies";
            string versionArgs = $@" --configuration {b.BuildConfiguration} /p:Version=""{b.NuGetVersion}"" /p:AssemblyVersion=""{b.AssemblyVersion}"" /p:FileVersion=""{b.FileVersion}"" /p:InformationalVersion=""{b.InformationalVersion}"" ";
            var args = info.MustPack
                        ? $@"pack --output ""{_localFeedProvider.ZeroBuild.PhysicalPath}"""
                        : $@"publish --output ""{_localFeedProvider.GetZeroVersionCodeCakeBuilderExecutablePath( solution.Name ).RemoveLastPart()}""";
            args += commonArgs + versionArgs;

            var path = GitFolder.FileSystem.GetFileInfo( msP.Path.RemoveLastPart() ).PhysicalPath;
            FileHelper.RawDeleteLocalDirectory( monitor, System.IO.Path.Combine( path, "bin" ) );
            FileHelper.RawDeleteLocalDirectory( monitor, System.IO.Path.Combine( path, "obj" ) );

            OnZeroBuildProject?.Invoke( this, new ZeroBuildEventArgs( monitor, true, info ) );
            try
            {
                return ProcessRunner.Run( monitor, path, "dotnet", args );
            }
            finally
            {
                GitFolder.ResetHard( monitor );
                OnZeroBuildProject?.Invoke( this, new ZeroBuildEventArgs( monitor, false, info ) );
            }
        }

        public bool IsUpgradeLocalPackagesEnabled => _world.WorkStatus == GlobalWorkStatus.Idle && IsActive;

        [CommandMethod]
        public bool UpgradeLocalPackages( IActivityMonitor monitor, bool upgradeBuildProjects )
        {
            if( !IsUpgradeLocalPackagesEnabled ) throw new InvalidOperationException( nameof( IsUpgradeLocalPackagesEnabled ) );

            var solution = GetSolution( monitor, allowInvalidSolution: false );
            if( solution == null ) return false;

            var feed = PluginBranch == StandardGitStatus.Local
                        ? _localFeedProvider.Local
                        : _localFeedProvider.CI;

            var toUpgrade = solution.Projects
                        .Where( p => upgradeBuildProjects || !p.IsBuildProject )
                        .SelectMany( p => p.PackageReferences )
                        .Select( dep => (Dep: dep, LocalVersion: feed.GetBestNuGetVersion( monitor, dep.Target.Artifact.Name )) )
                        .Where( pv => pv.LocalVersion != null )
                        .Select( pv => new UpdatePackageInfo( pv.Dep.Owner, NuGetType, pv.Dep.Target.Artifact.Name, pv.LocalVersion ) )
                        .ToList();

            if( !UpdatePackageDependencies( monitor, toUpgrade ) ) return false;
            return LocalCommit( monitor );
        }

        bool LocalCommit( IActivityMonitor m )
        {
            Debug.Assert( IsActive );
            bool amend = PluginBranch == StandardGitStatus.Local || GitFolder.Head.Message == "Local build auto commit.";
            return GitFolder.Commit( m, "Local build auto commit.", amend ? CommitBehavior.AmendIfPossibleAndOverwritePreviousMessage : CommitBehavior.CreateNewCommit );
        }

        /// <summary>
        /// Fires before a build.
        /// </summary>
        public event EventHandler<BuildStartEventArgs> OnStartBuild;

        /// <summary>
        /// Fires after a successful build.
        /// </summary>
        public event EventHandler<EventMonitoredArgs> OnBuildSucceed;

        /// <summary>
        /// Fires after a failed build.
        /// </summary>
        public event EventHandler<EventMonitoredArgs> OnBuildFailed;

        /// <summary>
        /// The solution must be valid (see <see cref="IsSolutionValid"/>) and all build secrets
        /// must be resolved.
        /// </summary>
        public bool IsBuildEnabled => _world.WorkStatus == GlobalWorkStatus.Idle
                                        && IsActive
                                        && _isSolutionValid
                                        && _buildSecrets.All( s => s.Secret != null );

        /// <summary>
        /// Builds the solution in 'local' branch or build in 'develop' without remotes, using the published Zero
        /// Version builder if it exists.
        /// This normally produces a CI build unless a version tag exists on the commit point.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="upgradeLocalDependencies">False to not upgrade the available local dependencies.</param>
        /// <param name="withUnitTest">False to skip unit tests.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Build( IActivityMonitor monitor, bool upgradeLocalDependencies = true, bool withUnitTest = true )
        {
            if( !IsBuildEnabled ) throw new InvalidOperationException( nameof( IsBuildEnabled ) );

            if( upgradeLocalDependencies )
            {
                if( !UpgradeLocalPackages( monitor, false ) ) return false;
            }
            else if( !LocalCommit( monitor ) )
            {
                return false;
            }

            return DoBuild( monitor, withUnitTest, null, false );
        }

        bool ISolutionDriver.Build( IActivityMonitor monitor, bool withUnitTest, bool? withZeroBuilder, bool withPushToRemote )
        {
            return DoBuild( monitor, withUnitTest, withZeroBuilder, withPushToRemote );
        }

        bool DoBuild( IActivityMonitor monitor, bool withUnitTest, bool? withZeroBuilder, bool withPushToRemote )
        {
            var solution = GetSolution( monitor, false );
            if( solution == null ) return false;

            // Version is always provided by the current commit point.
            var v = GitFolder.ReadRepositoryVersionInfo( monitor )?.FinalNuGetVersion;
            if( v == null ) return false;

            BuildType buildType;

            IEnvLocalFeed feed;
            if( PluginBranch == StandardGitStatus.Local )
            {
                if( !v.NormalizedText.EndsWith( "-local" ) )
                {
                    monitor.Warn( $"Version {v} is not a -local version. It has already been built." );
                    return true;
                }
                feed = _localFeedProvider.Local;
                buildType = BuildType.Local;
            }
            else if( v.AsCSVersion == null )
            {
                // Not a CSemVer version: it is a CI build.
                feed = _localFeedProvider.CI;
                buildType = BuildType.CI;
            }
            else
            {
                feed = _localFeedProvider.Release;
                buildType = BuildType.Release;
            }

            monitor.Info( $"Version to build: '{v}'." );

            var missing = feed.GetMissing( monitor, solution.GeneratedArtifacts.Select( g => g.Artifact.WithVersion( v ) ) );
            if( missing.Count == 0 )
            {
                monitor.Info( $"All artifacts are already available in {feed.PhysicalPath.LastPart} with version {v}: {solution.GeneratedArtifacts.Select( a => a.Artifact.ToString() ).Concatenate()}." );
                if( !withUnitTest )
                {
                    monitor.Info( $"No unit tests required. Build is skipped." );
                    return true;
                }
                if( _solutionSpec.NoDotNetUnitTests )
                {
                    monitor.Info( $"Solution settings: NoDotNetUnitTests is true. Build is skipped." );
                    return true;
                }
            }
            if( withUnitTest && !_solutionSpec.NoDotNetUnitTests ) buildType |= BuildType.WithUnitTests;
            if( withPushToRemote )
            {
                if( buildType == BuildType.CI )
                {
                    buildType |= BuildType.WithPushToRemote;
                }
                else
                {
                    if( buildType == BuildType.Local )
                    {
                        throw new ArgumentException( "Remote push is not allowed for 'local' builds.", nameof( withPushToRemote ) );
                    }
                    // The version is a 'release'. When releasing with CK-Env, no push to remotes are done (artifacts are
                    // retained and pushes are deferred).
                    // ==> This could be an ArgumentException just as above for the 'local' case.
                    // BUT! We may be in "normal CI Build" case and the version is 'release' because the commit has been
                    // already released on this system or on another one.
                    // On this system and if the local feeds have not been emptied, we have handled this by the 'skip handling' above.
                    // If not (from a fresh check out for instance), the only way to handle this would be to challenge the existence of
                    // the artifacts in the remotes which is a PITA: the 'skip handling' above would be costly.
                    // ==> We just warn and ignores the push.
                    monitor.Warn( $"Version '{v}' is not a CI version. Push to remote is ignored since it has already been done or will be done when publishing the release." );
                }
            }

            string ccbPath = _localFeedProvider.GetZeroVersionCodeCakeBuilderExecutablePath( solution.Name );

            if( System.IO.File.Exists( ccbPath ) )
            {
                if( withZeroBuilder != false )
                {
                    buildType |= BuildType.WithZeroBuilder;
                    monitor.Info( "Using available CodeCakeBuilder published Zero version." );
                }
            }
            else
            {
                if( withZeroBuilder == true )
                {
                    var msg = "CodeCakeBuilder Zero Version executable file not found";
                    monitor.Error( $"Invalid 'withZeroBuilder' constraint: {msg}. Zero Build versions must first be built." );
                    return false;
                }
                ccbPath = null;
            }
            if( (buildType & BuildType.WithZeroBuilder) != BuildType.WithZeroBuilder )
            {
                monitor.Info( "Using CodeCakeBuilder with source compilation (dotnet run)." );
            }
            var ev = new BuildStartEventArgs(
                            monitor,
                            missing.Count > 0,
                            solution,
                            v,
                            buildType,
                            GitFolder.FullPhysicalPath,
                            ccbPath );

            bool hasError = false;
            using( ev.Monitor.OnError( () => hasError = true ) )
            {
                OnStartBuild?.Invoke( this, ev );
            }
            if( hasError ) return false;
            var r = DoBuild( ev );
            if( r ) OnBuildSucceed?.Invoke( this, ev );
            else OnBuildFailed?.Invoke( this, ev );
            if( ev.IsUsingDirtyFolder ) GitFolder.ResetHard( ev.Monitor );
            return r;
        }

        bool DoBuild( BuildStartEventArgs ev )
        {
            IActivityMonitor m = ev.Monitor;
            using( m.OpenInfo( $"Building {ev.Solution}, Target Version = {ev.Version}" ) )
            {
                try
                {
                    ev.EnvironmentVariables.AddRange( _buildSecrets.Where( s => s.Secret != null ) );

                    var args = ev.WithZeroBuilder
                                ? ev.CodeCakeBuilderExecutableFile + " SolutionDirectoryIsCurrentDirectory"
                                : "run --project CodeCakeBuilder";
                    args += " -autointeraction";
                    args += " -PublishDirtyRepo=" + (ev.IsUsingDirtyFolder ? 'Y' : 'N');
                    args += " -PushToRemote=" + (ev.WithPushToRemote ? 'Y' : 'N');
                    if( !ev.BuildIsRequired ) args += " -target=\"Unit-Testing\" -exclusiveOptional -IgnoreNoArtifactsToProduce=Y";
                    if( !ev.WithUnitTest ) args += " -RunUnitTests=N";
                    if( !ProcessRunner.Run( m, ev.SolutionPhysicalPath, "dotnet", args, LogLevel.Warn, ev.EnvironmentVariables ) )
                    {
                        return false;
                    }
                }
                catch( Exception ex )
                {
                    m.Error( $"Build failed.", ex );
                    return false;
                }
                return true;
            }
        }
    }
}
