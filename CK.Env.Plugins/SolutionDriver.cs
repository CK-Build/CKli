using CK.Core;
using CK.Env.MSBuild;
using CK.Env.NPM;
using CK.Text;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugins
{
    public class SolutionDriver : GitBranchPluginBase, ISolutionDriver, IDisposable, ICommandMethodsProvider
    {
        readonly ISecretKeyStore _keyStore;
        readonly ISolutionDriverWorld _world;
        readonly IBranchSolutionLoader _solutionLoader;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly ISolutionSettings _settings;
        Solution _solution;

        public SolutionDriver(
            ISecretKeyStore keyStore,
            ISolutionDriverWorld w,
            GitFolder f,
            NormalizedPath branchPath,
            ISolutionSettings settings,
            IBranchSolutionLoader solutionLoader,
            IEnvLocalFeedProvider localFeedProvider )
            : base( f, branchPath )
        {
            w.Register( this );
            _world = w;
            _keyStore = keyStore;
            _solutionLoader = solutionLoader;
            _settings = settings;
            _localFeedProvider = localFeedProvider;
        }

        void IDisposable.Dispose()
        {
            _world.Unregister( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( "SolutionDriver" );

        IGitRepository ISolutionDriver.GitRepository => Folder;

        string ISolutionDriver.BranchName => BranchPath.LastPart;

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
            var (Success, ReloadNeeded) = Folder.Pull( m );
            if( !Success ) return false;
            return !ReloadNeeded || GetPrimarySolution( m, true ) != null;
        }

        /// <summary>
        /// Gets the solution driver of the <see cref="IGitRepository.CurrentBranchName"/>.
        /// </summary>
        /// <returns>This solution driver or the one of the current branch.</returns>
        public ISolutionDriver GetCurrentBranchDriver()
        {
            return Folder.StandardGitStatus == PluginBranch
                ? this
                : Folder.PluginManager.BranchPlugins[Folder.CurrentBranchName].GetPlugin<SolutionDriver>();
        }

        /// <summary>
        /// Loads or reloads the primary solution and its secondary solutions.
        /// If the solution has been reloaded (under the hood), the <see cref="Solution.Current"/> is returned:
        /// this ensures that the plugins always work with an up-to-date version of the solution.
        /// Use <paramref name="reload"/> sets to true only to actually reload the solution.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to force the reload of the solutions.</param>
        /// <returns>The primary solution or null.</returns>
        public Solution GetPrimarySolution( IActivityMonitor m, bool reload = false )
        {
            if( _solution == null || reload )
            {
                _solution = _solutionLoader.GetPrimarySolution( m, reload, BranchPath.LastPart );
                if( _solution == null )
                {
                    m.Error( $"Unable to load primary solution '{BranchPath}/{BranchPath.LastPart}.sln'." );
                }
            }
            else
            {
                var c = _solution.Current;
                if( c == null )
                {
                    m.Error( $"Unable to obtain primary solution '{BranchPath}/{BranchPath.LastPart}.sln' object since a reload of it has failed." );
                    return null;
                }
                if( c != _solution )
                {
                    m.Trace( $"Primary solution '{BranchPath}/{BranchPath.LastPart}.sln' refreshed due to successful reload." );
                    _solution = c;
                }
            }
            return _solution;
        }

        /// <summary>
        /// Gets all the solutions: first is the primary and then all the secondary's solutions.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>All the solutions or null on error.</returns>
        public IEnumerable<Solution> GetAllSolutions( IActivityMonitor monitor )
        {
            var primary = GetPrimarySolution( monitor );
            if( primary == null ) return null;
            return new[] { primary }.Concat( primary.LoadedSecondarySolutions );
        }

        /// <summary>
        /// Gets all NPM projects.
        /// </summary>
        /// <param name="m">The monitor use.</param>
        /// <returns>The set of NPM projects that are declared.</returns>
        public IEnumerable<NPMProject> GetAllNPMProjects( IActivityMonitor m ) => GetAllSolutions( m )?
                                                                           .SelectMany( s => s.NPMProjects )
                                                                           ?? Array.Empty<NPMProject>();

        /// <summary>
        /// Gets all the NPM projects defined in the solution as long as they are all
        /// valid: if any status is not <see cref="NPMProjectStatus.Valid"/>, a warning
        /// is emitted and null is returned.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The set of NPM projects (can be empty) or null on error.</returns>
        public IEnumerable<NPMProject> GetAllValidNPMProjects( IActivityMonitor m )
        {
            var all = GetAllSolutions( m )?.SelectMany( s => s.NPMProjects );
            if( all != null )
            {
                var invalid = all.Where( p => p.Status != NPMProjectStatus.Valid );
                if( invalid.Any() )
                {
                    m.Error( $"Unable to consider NPM projects since they must all be valid. Invalid projects: {invalid.Select( p => $"{p.FullPath} - {p.Status}" ).Concatenate()}" );
                    return null;
                }
            }
            return all;
        }

        /// <summary>
        /// Gets the set of solution names that this driver handles with the first one being the primary solution,
        /// followed by the secondary solutions if any.
        /// Returns null on any error that prevented the solutions to be loaded.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The solution names or null on error.</returns>
        public IEnumerable<string> GetSolutionNames( IActivityMonitor monitor )
        {
            var a = GetAllSolutions( monitor );
            return a?.Select( s => s.UniqueSolutionName );
        }

        /// <summary>
        /// Centralized way to know whether CKSetup components are produced by this solution.
        /// </summary>
        /// <param name="m">The monitor use.</param>
        /// <returns>Whether CKSetup components are produced, null on error.</returns>
        public bool? AreCKSetupComponentsProduced( IActivityMonitor m ) => !_settings.UseCKSetup
                                                                        ? false
                                                                        : GetAllSolutions( m )
                                                                          ?.SelectMany( s => s.CKSetupComponentProjects )
                                                                          .Any();

        /// <summary>
        /// Gets whether .Net packages are published.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>Null on error, true if at least one .Net packages is produced, false otherwise.</returns>
        public bool? HasDotnetPublishedProjects( IActivityMonitor m ) => GetPrimarySolution( m, false )?.PublishedProjects.Any();


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

            var primary = GetPrimarySolution( monitor );
            if( primary == null ) return false;

            var p = FindProject( monitor, primary, info.SolutionName, info.ProjectName, true );

            // This is required, otherwise the NuGet cache keeps the previous version (lost 4 hours of my life here).
            if( info.MustPack )
            {
                _localFeedProvider.RemoveFromNuGetCache( monitor, info.ProjectName, SVersion.ZeroVersion );
            }
            var p2pRefToRemove = p.Deps.Projects.Where( p2p => info.UpgradeZeroProjects.Contains( p2p.TargetProject.Name ) ).ToList();
            if( p2pRefToRemove.Count > 0 )
            {
                monitor.Info( $"Removing Project references: {p2pRefToRemove.Select( p2p => p2p.Element.ToString() ).Concatenate()}" );
                p2pRefToRemove.Select( p2p => p2p.Element ).Remove();
            }
            foreach( var z in info.UpgradeZeroProjects )
            {
                p.SetPackageReferenceVersion( monitor, p.TargetFrameworks, z, SVersion.ZeroVersion, true, false, false );
            }
            if( !p.Solution.Save( monitor ) ) return false;

            ICommitAssemblyBuildInfo b = CommitAssemblyBuildInfo.ZeroBuildInfo;
            string commonArgs = $@" --no-dependencies";
            string versionArgs = $@" --configuration {b.BuildConfiguration} /p:Version=""{b.NuGetVersion}"" /p:AssemblyVersion=""{b.AssemblyVersion}"" /p:FileVersion=""{b.FileVersion}"" /p:InformationalVersion=""{b.InformationalVersion}"" ";
            var args = info.MustPack
                        ? $@"pack --output ""{_localFeedProvider.ZeroBuild.PhysicalPath}"""
                        : $@"publish --output ""{_localFeedProvider.GetZeroVersionCodeCakeBuilderExecutablePath( primary.UniqueSolutionName ).RemoveLastPart()}""";
            args += commonArgs + versionArgs;

            var path = Folder.FileSystem.GetFileInfo( p.Path.RemoveLastPart() ).PhysicalPath;
            FileSystem.RawDeleteLocalDirectory( monitor, Path.Combine( path, "bin" ) );
            FileSystem.RawDeleteLocalDirectory( monitor, Path.Combine( path, "obj" ) );

            OnZeroBuildProject?.Invoke( this, new ZeroBuildEventArgs( monitor, true, info ) );
            try
            {
                return ProcessRunner.Run( monitor, path, "dotnet", args );
            }
            finally
            {
                OnZeroBuildProject?.Invoke( this, new ZeroBuildEventArgs( monitor, false, info ) );
            }
        }

        Project FindProject( IActivityMonitor monitor, Solution primary, string solutionName, string projectName, bool throwOnNotFound )
        {
            var s = primary.UniqueSolutionName == solutionName
                    ? primary
                    : primary.LoadedSecondarySolutions.FirstOrDefault( second => second.UniqueSolutionName == solutionName );
            if( s == null )
            {
                var msg = $"Unable to find solution '{solutionName}' in {primary}.";
                if( throwOnNotFound ) throw new InvalidOperationException( msg );
                monitor.Error( msg );
                return null;
            }
            var p = s.AllProjects.FirstOrDefault( proj => proj.Name == projectName );
            if( p == null )
            {
                var msg = $"Unable to find project '{projectName}' in solution '{s}'.";
                if( throwOnNotFound ) throw new Exception( msg );
                monitor.Error( msg );
            }
            return p;
        }

        /// <summary>
        /// Updates projects dependencies and saves the solution and its updated projects.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageInfos">The packages to update.</param>
        /// <returns>True on success, false on error.</returns>
        public bool UpdatePackageDependencies( IActivityMonitor monitor, IEnumerable<UpdatePackageInfo> packageInfos )
        {
            var primary = GetPrimarySolution( monitor );
            if( primary == null ) return false;
            var toSave = new List<Solution>();
            foreach( var update in packageInfos )
            {
                var p = FindProject( monitor, primary, update.SolutionName, update.ProjectName, true );
                int changes = p.SetPackageReferenceVersion( monitor, p.TargetFrameworks, update.PackageUpdate.Artifact.Name, update.PackageUpdate.Version );
                if( changes != 0 && !toSave.Contains( p.Solution ) ) toSave.Add( p.Solution );
            }
            foreach( var solution in toSave )
            {
                if( !solution.Save( monitor ) ) return false;
            }
            return true;
        }

        bool LocalCommit( IActivityMonitor m )
        {
            Debug.Assert( IsActive );
            bool amend = PluginBranch == StandardGitStatus.Local || Folder.Head.Message == "Local build auto commit.";
            return Folder.Commit( m, "Local build auto commit.", amend );
        }

        /// <summary>
        /// Gets whether this plugin is able to work.
        /// It provides services only on local or develop and if the <see cref="GitFolder.StandardGitStatus"/>
        /// is the same as <see cref="GitBranchPluginBase.PluginBranch"/>.
        /// </summary>
        bool IsActive => Folder.StandardGitStatus == PluginBranch
                         && (PluginBranch == StandardGitStatus.Local || PluginBranch == StandardGitStatus.Develop);

        public bool IsUpgradeLocalPackagesEnabled => _world.WorkStatus == GlobalWorkStatus.Idle && IsActive;

        [CommandMethod]
        public bool UpgradeLocalPackages( IActivityMonitor monitor, bool upgradeBuildProjects )
        {
            if( !IsUpgradeLocalPackagesEnabled ) throw new InvalidOperationException( nameof( IsUpgradeLocalPackagesEnabled ) );

            var allSolutions = GetAllSolutions( monitor );
            if( allSolutions == null ) return false;

            var feed = PluginBranch == StandardGitStatus.Local
                        ? _localFeedProvider.Local
                        : _localFeedProvider.CI;

            var toUpgrade = allSolutions
                        .SelectMany( s => s.AllProjects )
                        .Where( p => upgradeBuildProjects || !p.IsBuildProject )
                        .SelectMany( p => p.Deps.Packages )
                        .Select( dep => (Dep: dep, LocalVersion: feed.GetBestVersion( monitor, dep.PackageId )) )
                        .Where( pv => pv.LocalVersion != null )
                        .Select( pv => new UpdatePackageInfo( pv.Dep.Owner.Solution.UniqueSolutionName, pv.Dep.Owner.Name, "NuGet", pv.Dep.PackageId, pv.LocalVersion ) );

            if( !UpdatePackageDependencies( monitor, toUpgrade ) ) return false;
            return LocalCommit( monitor );
        }

        public bool IsBuildEnabled => _world.WorkStatus == GlobalWorkStatus.Idle && IsActive;

        /// <summary>
        /// Builds the solution in 'local' branch or build in 'develop' without remotes, using the published Zero Version builder
        /// if it exists.
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
            var primary = GetPrimarySolution( monitor );
            if( primary == null ) return false;
            var allSolutions = GetAllSolutions( monitor ).ToArray();

            // Version is always provided by the current commit point.
            var v = Folder.ReadRepositoryVersionInfo( monitor )?.FinalNuGetVersion;
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

            var nuGetPackages = allSolutions.SelectMany( s => s.PublishedProjects )
                                            .Select( p => new ArtifactInstance( "NuGet", p.Name, v ) );
            var ckSetupComponents = allSolutions.SelectMany( s => s.CKSetupComponentProjects )
                                                .SelectMany( p => p.TargetFrameworks.AtomicTraits
                                                                   .Select( t => new ArtifactInstance( "CKSetup", p.Name + '/' + t.ToString(), v ) ) );
            var allArtifacts = nuGetPackages.Concat( ckSetupComponents );
            var missing = feed.GetMissing( monitor, allArtifacts );
            if( missing.Count == 0 )
            {
                monitor.Info( $"All artifacts are already available in {feed.PhysicalPath.LastPart} with version {v}: {allArtifacts.Select( a => a.Artifact.ToString() ).Concatenate()}." );
                if( !withUnitTest )
                {
                    monitor.Info( $"No unit tests required. Build is skipped." );
                    return true;
                }
                if( _settings.NoUnitTests )
                {
                    monitor.Info( $"Solution settings: NoUnitTests is true. Build is skipped." );
                    return true;
                }
            }
            if( withUnitTest && !_settings.NoUnitTests ) buildType |= BuildType.WithUnitTests;
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
                    // retained and pushes are defferred).
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

            string ccbPath = _localFeedProvider.GetZeroVersionCodeCakeBuilderExecutablePath( primary.UniqueSolutionName );

            if( File.Exists( ccbPath ) )
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
                            primary,
                            v,
                            buildType,
                            primary.GitFolder.FullPhysicalPath,
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
            if( ev.IsUsingDirtyFolder ) Folder.ResetHard( ev.Monitor );
            return r;
        }

        bool DoBuild( BuildStartEventArgs ev )
        {
            IActivityMonitor m = ev.Monitor;
            using( m.OpenInfo( $"Building {ev.PrimarySolution}, Target Version = {ev.Version}" ) )
            {
                try
                {
                    string key = _keyStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", false, "Required to execute CodeCakeBuilder." );
                    if( key == null ) return false;
                    ev.EnvironmentVariables.Add( ("CODECAKEBUILDER_SECRET_KEY", key) );

                    var args = ev.WithZeroBuilder
                                ? ev.CodeCakeBuilderExecutableFile + " SolutionDirectoryIsCurrentDirectory"
                                : "run --project CodeCakeBuilder";
                    args += " -autointeraction";
                    args += " -PublishDirtyRepo=" + (ev.IsUsingDirtyFolder ? 'Y' : 'N');
                    args += " -PushToRemote=" + (ev.WithPushToRemote ? 'Y' : 'N');
                    if( !ev.BuildIsRequired ) args += " -target=\"Unit-Testing\" -exclusiveOptional -IgnoreNoPackagesToProduce=Y";
                    if( !ev.WithUnitTest ) args += " -RunUnitTests=N";
                    if( !ProcessRunner.Run( m, ev.SolutionPhysicalPath, "dotnet", args, ev.EnvironmentVariables ) )
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

        [CommandMethod]
        public bool DumpLogsBetweenDates( IActivityMonitor m, string beginning, string ending )
        {
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
            m.Info( "Parsed beginning date: " + beginningDate.ToString() );
            m.Info( "Parsed ending date: " + endingDate.ToString() );
            var solution = GetPrimarySolution( m );
            Folder.ShowLogsBetweenDates( m, beginningDate, endingDate,
                solution.AllProjects
                    .Select( p => p.PrimarySolutionRelativeFolderPath )
                    .Union(
                        solution.NPMProjects.Select( p => p.FullPath )
                    ).ToList() );
            return true;
        }
    }
}
