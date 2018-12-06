using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;

namespace CK.Env.Plugins
{
    public class SolutionDriver : IGitBranchPlugin, ISolutionDriver, IDisposable, ICommandMethodsProvider
    {
        readonly ISecretKeyStore _keyStore;
        readonly WorldState _worldState;
        readonly IBranchSolutionLoader _solutionLoader;
        readonly ILocalFeedProvider _localFeedProvider;
        readonly ISolutionSettings _settings;
        Solution _solution;

        public SolutionDriver(
            ISecretKeyStore keyStore,
            WorldState w,
            GitFolder f,
            NormalizedPath branchPath,
            ISolutionSettings settings,
            IBranchSolutionLoader solutionLoader,
            ILocalFeedProvider localFeedProvider )
        {
            w.Initializing += OnWorldInitializing;
            _worldState = w;
            _keyStore = keyStore;
            BranchPath = branchPath;
            Folder = f;
            _solutionLoader = solutionLoader;
            _settings = settings;
            _localFeedProvider = localFeedProvider;
        }

        void OnWorldInitializing( object sender, WorldState.InitializingEventArgs e )
        {
            e.Register( this );
        }

        void IDisposable.Dispose()
        {
            _worldState.Initializing -= OnWorldInitializing;
        }

        public NormalizedPath BranchPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( "SolutionDriver" );

        IGitRepository ISolutionDriver.GitRepository => Folder;

        string ISolutionDriver.BranchName => BranchPath.LastPart;

        public GitFolder Folder { get; }

        /// <summary>
        /// Loads or reloads the <see cref="PrimarySolution"/> and its secondary solutions.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="reload">True to force the reload of the solutions.</param>
        /// <returns>The primary solution or null.</returns>
        public Solution EnsureLoaded( IActivityMonitor m, bool reload = false )
        {
            if( _solution == null )
            {
                _solution = _solutionLoader.GetPrimarySolution( m, reload, BranchPath.LastPart );
                if( _solution == null )
                {
                    m.Error( $"Unable to load primary solution '{BranchPath}/{BranchPath.LastPart}.sln'." );
                }
            }
            return _solution;
        }

        /// <summary>
        /// Gets the already loaded (see <see cref="EnsureLoaded(IActivityMonitor, bool)"/>) primary solution
        /// and its secondary solutions.
        /// </summary>
        public Solution PrimarySolution => _solution;

        public bool IsOnLocalBranch => BranchPath.LastPart == Folder.World.LocalBranchName;

        public bool IsOnDevelopBranch => BranchPath.LastPart == Folder.World.DevelopBranchName;

        IEnumerable<Solution> GetAllSolutions( IActivityMonitor monitor )
        {
            var primary = EnsureLoaded( monitor );
            if( primary == null ) return null;
            return new[] { primary }.Concat( primary.LoadedSecondarySolutions );
        }

        /// <summary>
        /// Gets the set of soltion names that this driver handles with the first one being the primary solution,
        /// followed by the secondary solutions if any.
        /// Returns null on any error that prevented the solutions to be loaded.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The solution names or null on error.</returns>
        public IEnumerable<string> GetSolutionNames( IActivityMonitor monitor )
        {
            var a = GetAllSolutions( monitor );
            return a == null ? null : a.Select( s => s.UniqueSolutionName );
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
        /// Fires before <see cref="ZeroBuildProject"/> actually builds a build project in ZeroVersion.
        /// </summary>
        public event EventHandler<EventMonitoredArgs> OnZeroBuildProject;

        /// <summary>
        /// Builds the given project (that must be handled by this driver otherwise an exception is thrown)
        /// with a zero version.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="packageInfos">The packages to update.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ZeroBuildProject( IActivityMonitor monitor, ZeroBuildProjectInfo info )
        {
            if( info == null ) throw new ArgumentNullException( nameof( info ) );
            var primary = EnsureLoaded( monitor );
            if( primary == null ) return false;

            var p = FindProject( monitor, primary, info.SolutionName, info.ProjectName, true );

            bool onLocal = IsOnLocalBranch && Folder.StandardGitStatus == StandardGitStatus.LocalBranch;
            Debug.Assert( onLocal || (IsOnDevelopBranch && Folder.StandardGitStatus == StandardGitStatus.DevelopBranch) );

            var targetFolder = onLocal
                                ? _localFeedProvider.GetLocalFeedFolder( monitor )
                                : _localFeedProvider.GetCIFeedFolder( monitor );

            using( Folder.OpenProtectedScope( monitor ) )
            {
                int changes = 0;
                foreach( var packageName in info.BuildPackageDependencies )
                {
                    changes += p.SetPackageReferenceVersion( monitor, p.TargetFrameworks, packageName, SVersion.ZeroVersion );
                }
                if( changes != 0 ) p.Solution.Save( monitor );

                var zeroVersionArgs = $@" --configuration Debug /p:Version=""{SVersion.ZeroVersion}"" /p:AssemblyVersion=""{InformationalVersion.ZeroAssemblyVersion}"" /p:FileVersion=""{InformationalVersion.ZeroFileVersion}"" /p:InformationalVersion=""{InformationalVersion.ZeroInformationalVersion}"" ";
                var args = info.MustPack
                            ? $@"pack --output ""{targetFolder.PhysicalPath}""" + zeroVersionArgs
                            : $@"build" + zeroVersionArgs;

                var path = Folder.FileSystem.GetFileInfo( p.Path.RemoveLastPart() ).PhysicalPath;
                Folder.FileSystem.RawDeleteLocalDirectory( monitor, System.IO.Path.Combine( path, "bin" ) );
                Folder.FileSystem.RawDeleteLocalDirectory( monitor, System.IO.Path.Combine( path, "obj" ) );

                OnZeroBuildProject?.Invoke( this, new EventMonitoredArgs( monitor ) );

                return ProcessRunner.Run( monitor, path, "dotnet", args );
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
            var primary = EnsureLoaded( monitor );
            if( primary == null ) return false;
            var toSave = new List<Solution>();
            foreach( var update in packageInfos )
            {
                var p = FindProject( monitor, primary, update.SolutionName, update.ProjectName, true );
                int changes = p.SetPackageReferenceVersion( monitor, p.TargetFrameworks, update.PackageUpdate.PackageId, update.PackageUpdate.Version );
                if( changes != 0 && !toSave.Contains( p.Solution ) ) toSave.Add( p.Solution );
            }
            foreach( var solution in toSave )
            {
                if( !solution.Save( monitor ) ) return false;
            }
            return true;
        }

        IEnumerable<UpdatePackageInfo> GetLocalUpgradePackages( IActivityMonitor monitor, bool withBuildProject )
        {
            var allSolutions = GetAllSolutions( monitor );
            if( allSolutions == null ) return null;
            bool onLocal = IsOnLocalBranch && Folder.StandardGitStatus == StandardGitStatus.LocalBranch;
            Debug.Assert( onLocal || (IsOnDevelopBranch && Folder.StandardGitStatus == StandardGitStatus.DevelopBranch) );
            return allSolutions
                    .SelectMany( s => s.AllProjects )
                    .Where( p => withBuildProject || !p.IsBuildProject )
                    .SelectMany( p => p.Deps.Packages )
                    .Select( dep => (Dep: dep, LocalVersion: onLocal
                                                            ? _localFeedProvider.GetBestLocalVersion( monitor, dep.PackageId )
                                                            : _localFeedProvider.GetBestLocalCIVersion( monitor, dep.PackageId )) )
                    .Where( pv => pv.LocalVersion != null )
                    .Select( pv => new UpdatePackageInfo( pv.Dep.Owner.Solution.UniqueSolutionName, pv.Dep.Owner.Name, pv.Dep.PackageId, pv.LocalVersion ) );
        }

        bool LocalCommit( IActivityMonitor m )
        {
            bool onLocal = IsOnLocalBranch && Folder.StandardGitStatus == StandardGitStatus.LocalBranch;
            Debug.Assert( onLocal || (IsOnDevelopBranch && Folder.StandardGitStatus == StandardGitStatus.DevelopBranch) );
            return onLocal
                    ? Folder.AmendCommit( m ).Success
                    : Folder.Commit( m, "Local build auto commit." ).Success;
        }

        bool IsActive => (IsOnLocalBranch && Folder.StandardGitStatus == StandardGitStatus.LocalBranch)
                         || (IsOnDevelopBranch && Folder.StandardGitStatus == StandardGitStatus.DevelopBranch);

        public bool IsUpgradeLocalPackagesEnabled => _worldState.WorkStatus == GlobalWorkStatus.Idle && IsActive;

        [CommandMethod]
        public bool UpgradeLocalPackages( IActivityMonitor monitor, bool upgradeBuildProjects )
        {
            if( !IsUpgradeLocalPackagesEnabled ) throw new InvalidOperationException( nameof( IsUpgradeLocalPackagesEnabled ) );
            var toUpgrade = GetLocalUpgradePackages( monitor, upgradeBuildProjects );
            if( toUpgrade == null ) return false;
            if( !UpdatePackageDependencies( monitor, toUpgrade ) ) return false;
            return LocalCommit( monitor );
        }

        public bool IsLocalBuildEnabled => _worldState.WorkStatus == GlobalWorkStatus.Idle && IsActive;

        /// <summary>
        /// Builds the solution in 'local' branch or build in 'develop' without remotes.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="upgradeLocalDependencies">False to not upgrade the available local dependencies.</param>
        /// <param name="withUnitTest">False to skip unit tests.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool LocalBuild( IActivityMonitor monitor, bool upgradeLocalDependencies = true, bool withUnitTest = true )
        {
            if( !IsLocalBuildEnabled ) throw new InvalidOperationException( nameof( IsLocalBuildEnabled ) );
            var buildType = IsOnLocalBranch ? BuildType.Local : BuildType.LocalDevelop;
            return DoLocalBuild( monitor, upgradeLocalDependencies, withUnitTest, buildType );
        }

        bool ISolutionDriver.LocalBuild(IActivityMonitor monitor, bool upgradeLocalDependencies, bool withUnitTest)
        {
            var buildType = _worldState.WorkStatus == GlobalWorkStatus.SwitchingToDevelop
                            ? BuildType.SwitchToDevelop
                            : (IsOnLocalBranch
                                ? BuildType.Local
                                : BuildType.LocalDevelop);
            return DoLocalBuild( monitor, upgradeLocalDependencies, withUnitTest, buildType );
        }

        bool DoLocalBuild( IActivityMonitor monitor, bool upgradeLocalDependencies, bool withUnitTest, BuildType buildType )
        {
            var primary = EnsureLoaded( monitor );
            if( primary == null ) return false;

            if( upgradeLocalDependencies )
            {
                if( !UpgradeLocalPackages( monitor, false ) ) return false;
            }
            else if( !LocalCommit( monitor ) ) return false;

            var v = Folder.ReadRepositoryVersionInfo( monitor )?.FinalNuGetVersion;
            if( v == null ) return false;

            var publishedNames = primary.LoadedSecondarySolutions.Append( primary )
                                            .SelectMany( s => s.PublishedProjects )
                                            .Select( p => p.Name );
            bool buildRequired = publishedNames.Any( p => _localFeedProvider.GetLocalPackage( monitor, p, v ) == null );
            if( !buildRequired )
            {
                monitor.Info( $"All {publishedNames.Count()} packages are already published in version {v}: {publishedNames.Concatenate()}." );
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
            var ev = new BuildStartEventArgs(
                            monitor,
                            buildRequired,
                            primary,
                            withUnitTest,
                            v,
                            buildType,
                            false );
            return RunBuild( ev );
        }

        bool RunBuild( BuildStartEventArgs ev )
        {
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

        bool DoBuild(
            BuildStartEventArgs ev,
            bool buildCodeCakeBuilder = false )
        {
            IActivityMonitor m = ev.Monitor;
            using( m.OpenInfo( $"Target Version = {ev.Version}" ) )
            {
                try
                {
                    string key = _keyStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", false, "Required to execute CodeCakeBuilder." );
                    if( key == null ) return false;
                    ev.EnvironmentVariables.Add( ("CODECAKEBUILDER_SECRET_KEY", key) );

                    var args = ev.BuildCodeCakeBuilderIsRequired ? "run --project CodeCakeBuilder" : ev.CodeCakeBuilderExecutablePhysicalPath;
                    args += " -autointeraction";
                    args += " -PublishDirtyRepo=" + (ev.IsUsingDirtyFolder ? 'Y' : 'N');
                    if( !ev.BuildIsRequired ) args += " -target=\"Unit-Testing\" -exclusiveOptional -IgnoreNoPackagesToProduce=Y";
                    if( !ev.WithUnitTest ) args += " -RunUnitTests=N";
                    if( !ProcessRunner.Run( m, ev.SolutionFolderPhysicalPath, "dotnet", args, ev.EnvironmentVariables ) )
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
