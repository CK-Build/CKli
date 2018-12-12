using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CKSetupStoreTestHelperConfigFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider, IDisposable
    {
        readonly ISolutionSettings _settings;
        readonly SolutionDriver _solutionDriver;
        readonly IEnvLocalFeedProvider _localFeedProvider;

        public CKSetupStoreTestHelperConfigFile(
            GitFolder f,
            SolutionDriver solutionDriver,
            ISolutionSettings settings,
            IEnvLocalFeedProvider localFeedProvider,
            NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( "RemoteStore.TestHelper.config" ) )
        {
            _settings = settings;
            _solutionDriver = solutionDriver;
            _localFeedProvider = localFeedProvider;
            // Always monitor branch change even if _settings.ProduceCKSetupComponents is false
            // so that we delete the test file on leaving local if it happens to exist.
            if( PluginBranch == StandardGitStatus.Local )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
            // If the settings states that CKSetup is not used, there is no need to react to builds.
            if( _settings.ProduceCKSetupComponents )
            {
                _solutionDriver.OnStartBuild += OnStartBuild;
                _solutionDriver.OnBuildSucceed += OnBuildEnd;
                _solutionDriver.OnBuildFailed += OnBuildEnd;
            }
        }

        void OnStartBuild( object sender, BuildStartEventArgs e )
        {
            Debug.Assert( _settings.ProduceCKSetupComponents );
            if( !e.IsUsingDirtyFolder ) return;

            // The CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL environment variable is always required
            // except when building in 'remote develop' and 'local'.
            // The Build script must handle these two cases (so that CodeCakebuilder can be run
            // directly): on 'develop', the actual remote is used normally and in local, the script
            // MUST map the local store. Typically:
            //
            //    if( globalInfo.IsLocalCIRelease )
            //    {
            //        storeConf.TargetStoreUrl = System.IO.Path.Combine( globalInfo.LocalFeedPath, "CKSetupStore" );
            //    }
            //

            // Ensures that the local stores exist.
            if( !_localFeedProvider.EnsureCKSetupStores( e.Monitor ) ) return;

            var targetStore = _localFeedProvider.GetCKSetupStorePath( e.Monitor, e.BuildType );
            if( !_settings.NoUnitTests )
            {
                EnsureStorePath( e.Monitor, targetStore );
            }
            e.EnvironmentVariables.Add( ("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", targetStore) );
        }

        void OnBuildEnd( object sender, EventMonitoredArgs e )
        {
            // This is an untracked file. It has to be removed, even with BuildType.IsUsingDirtyFolder
            // (except of course on the local branch).
            if( PluginBranch != StandardGitStatus.Local ) Delete( e.Monitor );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            if( PluginBranch == StandardGitStatus.Local && _settings.ProduceCKSetupComponents && !_settings.NoUnitTests )
            {
                EnsureStorePath( m, _localFeedProvider.Local.PhysicalPath );
            }
            else
            {
                Delete( m );
            }
        }

        public void Dispose()
        {
            if( PluginBranch == StandardGitStatus.Local )
            {
                Folder.OnLocalBranchEntered -= OnLocalBranchEntered;
                Folder.OnLocalBranchLeaving -= OnLocalBranchLeaving;
            }
        }

        void OnLocalBranchLeaving( object sender, EventMonitoredArgs e ) => Delete( e.Monitor );

        void OnLocalBranchEntered( object sender, EventMonitoredArgs e )
        {
            if( _settings.ProduceCKSetupComponents ) EnsureStorePath( e.Monitor, _localFeedProvider.Local.PhysicalPath );
        }

        public bool EnsureStorePath( IActivityMonitor m, string storePath )
        {
            if( !_localFeedProvider.EnsureCKSetupStores( m ) ) return false;
            var text = "<configuration><appSettings>" + Environment.NewLine;
            text += "  -- This forces the Solutions that generate components to use the LocalFeed CKSetupStore" + Environment.NewLine; ;
            text += $@"  <add key=""CKSetup/DefaultStoreUrl"" value=""{storePath}"" />" + Environment.NewLine;
            text += $@"  <add key=""CKSetup/DefaultStorePath"" value=""{storePath}"" />" + Environment.NewLine;
            text += "</appSettings></configuration>";
            return CreateOrUpdate( m, text );
        }
    }
}
