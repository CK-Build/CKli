using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Env.MSBuild;
using CK.Text;
using CSemVer;

namespace CK.Env.Plugins
{
    public class SolutionDriver : GitBranchPluginBase, ISolutionDriver, IDisposable, ICommandMethodsProvider
    {
        readonly ISecretKeyStore _keyStore;
        readonly WorldState _worldState;
        readonly IBranchSolutionLoader _solutionLoader;
        readonly IEnvLocalFeedProvider _localFeedProvider;
        readonly ISolutionSettings _settings;
        Solution _solution;

        public SolutionDriver(
            ISecretKeyStore keyStore,
            WorldState w,
            GitFolder f,
            NormalizedPath branchPath,
            ISolutionSettings settings,
            IBranchSolutionLoader solutionLoader,
            IEnvLocalFeedProvider localFeedProvider )
            : base( f, branchPath )
        {
            w.Initializing += OnWorldInitializing;
            _worldState = w;
            _keyStore = keyStore;
            _solutionLoader = solutionLoader;
            _settings = settings;
            _localFeedProvider = localFeedProvider;
            LocalFeed = localFeedProvider.GetFeed( PluginBranch );
        }

        void OnWorldInitializing( object sender, WorldState.InitializingEventArgs e )
        {
            e.Register( this );
        }

        void IDisposable.Dispose()
        {
            _worldState.Initializing -= OnWorldInitializing;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => BranchPath.AppendPart( "SolutionDriver" );

        IGitRepository ISolutionDriver.GitRepository => Folder;

        string ISolutionDriver.BranchName => BranchPath.LastPart;

        /// <summary>
        /// Gets the local feed if the <see cref="GitBranchPluginBase.PluginBranch"/> is one of
        /// the 3 standard branches. Null otherwise.
        /// </summary>
        public IEnvLocalFeed LocalFeed { get; }

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
            if( _solution == null )
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

        IEnumerable<Solution> GetAllSolutions( IActivityMonitor monitor )
        {
            var primary = GetPrimarySolution( monitor );
            if( primary == null ) return null;
            return new[] { primary }.Concat( primary.LoadedSecondarySolutions );
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
            if( !IsActive ) throw new InvalidOperationException( nameof( IsActive ) );

            var primary = GetPrimarySolution( monitor );
            if( primary == null ) return false;

            var p = FindProject( monitor, primary, info.SolutionName, info.ProjectName, true );
            if( info.MustPack )
            {
                _localFeedProvider.RemoveFromNuGetCache( monitor, info.ProjectName, SVersion.ZeroVersion );
            }
            using( Folder.OpenProtectedScope( monitor ) )
            {
                int changes = 0;
                foreach( var packageName in info.UpgradePackages )
                {
                    changes += p.SetPackageReferenceVersion( monitor, p.TargetFrameworks, packageName, SVersion.ZeroVersion );
                }
                if( changes != 0 ) p.Solution.Save( monitor );

                var zeroVersionArgs = $@" --configuration Debug /p:Version=""{SVersion.ZeroVersion}"" /p:AssemblyVersion=""{InformationalVersion.ZeroAssemblyVersion}"" /p:FileVersion=""{InformationalVersion.ZeroFileVersion}"" /p:InformationalVersion=""{InformationalVersion.ZeroInformationalVersion}"" ";
                var args = info.MustPack
                            ? $@"pack --output ""{LocalFeed.PhysicalPath}""" + zeroVersionArgs
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
            var primary = GetPrimarySolution( monitor );
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

            return allSolutions
                    .SelectMany( s => s.AllProjects )
                    .Where( p => withBuildProject || !p.IsBuildProject )
                    .SelectMany( p => p.Deps.Packages )
                    .Select( dep => (Dep: dep, LocalVersion: LocalFeed.GetBestVersion( monitor, dep.PackageId )) )
                    .Where( pv => pv.LocalVersion != null )
                    .Select( pv => new UpdatePackageInfo( pv.Dep.Owner.Solution.UniqueSolutionName, pv.Dep.Owner.Name, pv.Dep.PackageId, pv.LocalVersion ) );
        }

        bool LocalCommit( IActivityMonitor m )
        {
            Debug.Assert( IsActive );
            return PluginBranch == StandardGitStatus.Local
                    ? Folder.AmendCommit( m ).Success
                    : Folder.Commit( m, "Local build auto commit." ).Success;
        }

        /// <summary>
        /// Gets whether this plugin is able to work.
        /// It provides services only on local or develop and if the <see cref="GitFolder.StandardGitStatus"/>
        /// is the same as <see cref="GitBranchPluginBase.PluginBranch"/>.
        /// </summary>
        bool IsActive => Folder.StandardGitStatus == PluginBranch
                         && (PluginBranch == StandardGitStatus.Local || PluginBranch != StandardGitStatus.Develop);

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
            var buildType = PluginBranch == StandardGitStatus.Local ? BuildType.Local : BuildType.Develop;
            return DoBuild( monitor, upgradeLocalDependencies, withUnitTest, buildType );
        }

        bool ISolutionDriver.Build( IActivityMonitor monitor, bool upgradeLocalDependencies, bool withUnitTest )
        {
            var buildType = _worldState.WorkStatus == GlobalWorkStatus.SwitchingToDevelop
                            ? BuildType.SwitchToDevelop
                            : (PluginBranch == StandardGitStatus.Local
                                ? BuildType.Local
                                : BuildType.Develop);
            return DoBuild( monitor, upgradeLocalDependencies, withUnitTest, buildType );
        }

        bool DoBuild( IActivityMonitor monitor, bool upgradeLocalDependencies, bool withUnitTest, BuildType buildType )
        {
            var primary = GetPrimarySolution( monitor );
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
            bool buildRequired = publishedNames.Any( p => _localFeedProvider.Local.GetPackageFile( monitor, p, v ) == null );
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
