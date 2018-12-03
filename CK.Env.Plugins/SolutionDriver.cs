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
        Solution _solution;

        public SolutionDriver(
            ISecretKeyStore keyStore,
            WorldState w,
            GitFolder f,
            NormalizedPath branchPath,
            IBranchSolutionLoader solutionLoader )
        {
            _worldState = w;
            _keyStore = keyStore;
            w.Initializing += OnWorldInitializing;
            BranchPath = branchPath;
            Folder = f;
            _solutionLoader = solutionLoader;
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
                var s = primary.UniqueSolutionName == update.SolutionName
                        ? primary
                        : primary.LoadedSecondarySolutions.FirstOrDefault( second => second.UniqueSolutionName == update.SolutionName );
                if( s == null )
                {
                    throw new Exception( $"Unable to find solution '{update.SolutionName}' in {primary}." );
                }
                var p = s.AllProjects.FirstOrDefault( proj => proj.Name == update.ProjectName );
                if( p == null )
                {
                    throw new Exception( $"Unable to find project '{update.ProjectName}' in solution '{s}'." );
                }
                int changes = p.SetPackageReferenceVersion( monitor, p.TargetFrameworks, update.PackageUpdate.PackageId, update.PackageUpdate.Version );
                if( changes != 0 && !toSave.Contains( s ) ) toSave.Add( s );
            }
            foreach( var solution in toSave )
            {
                if( !solution.Save( monitor ) ) return false;
            }
            return true;
        }

        public bool IsOnLocalBranch => BranchPath.LastPart == Folder.World.LocalBranchName;

        public bool IsCILocalBuildEnabled => IsOnLocalBranch && Folder.StandardGitStatus == StandardGitStatus.LocalBranch;

        [CommandMethod]
        public bool CILocalBuild( IActivityMonitor monitor, bool withUnitTest = true )
        {
            var versionInfo = Folder.ReadRepositoryVersionInfo( monitor );
            if( versionInfo == null ) return false;
            return DoBuild( monitor, withUnitTest, versionInfo.FinalNuGetVersion, true );
        }

        bool DoBuild( IActivityMonitor monitor, bool withUnitTest, SVersion v, bool buildRequired )
        {
            Debug.Assert( buildRequired || withUnitTest );
            var primary = EnsureLoaded( monitor );
            if( primary == null ) return false;

            string key = _keyStore.GetSecretKey( monitor, "CODECAKEBUILDER_SECRET_KEY", false, "Required to execute CodeCakeBuilder." );
            if( key == null ) return false;
            var environmentVariables = new[] { ("CODECAKEBUILDER_SECRET_KEY", key) };

            using( monitor.OpenInfo( $"Target Version = {v}" ) )
            {
                var path = Folder.FileSystem.GetFileInfo( primary.SolutionFolderPath ).PhysicalPath;
                var args = "run --project CodeCakeBuilder -autointeraction";
                if( !buildRequired ) args += " -target=\"Unit-Testing\" -exclusiveOptional -IgnoreNoPackagesToProduce=Y";
                if( !withUnitTest ) args += " -RunUnitTests=N";
                try
                {
                    if( !ProcessRunner.Run( monitor, path, "dotnet", args, environmentVariables ) )
                    {
                        return false;
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( $"Build failed.", ex );
                    return false;
                }
                return true;
            }
        }
    }
}
