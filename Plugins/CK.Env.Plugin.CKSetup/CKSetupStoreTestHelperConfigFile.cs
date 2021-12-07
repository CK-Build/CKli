using CK.Core;

using System;
using System.Diagnostics;

namespace CK.Env.Plugin
{
    /// <summary>
    /// This is temporary waiting for the CKSetup v13 to be used everywhere: v13 uses the CKSetupStore.txt redirection file
    /// for its tests as well as for its normal run.
    /// </summary>
    public class CKSetupStoreTestHelperConfigFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider, IDisposable
    {
        readonly SolutionSpec _solutionSpec;
        readonly SolutionDriver _solutionDriver;
        readonly IEnvLocalFeedProvider _localFeedProvider;

        public CKSetupStoreTestHelperConfigFile(
            GitRepository f,
            SolutionDriver solutionDriver,
            SolutionSpec settings,
            IEnvLocalFeedProvider localFeedProvider,
            NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( "RemoteStore.TestHelper.config" ) )
        {
            _solutionSpec = settings;
            _solutionDriver = solutionDriver;
            _localFeedProvider = localFeedProvider;
            // Always monitor branch change even if _settings.ProduceCKSetupComponents is false
            // so that we delete the test file on leaving local if it happens to exist.
            if( StandardPluginBranch == StandardGitStatus.Local )
            {
                f.OnLocalBranchEntered += OnLocalBranchEntered;
                f.OnLocalBranchLeaving += OnLocalBranchLeaving;
            }
            // If the settings states that CKSetup is not used, there is no need to react to builds.
            if( _solutionSpec.UseCKSetup )
            {
                _solutionDriver.OnStartBuild += OnStartBuild;
                _solutionDriver.OnEndBuild += OnEndBuild;
            }
        }

        void OnStartBuild( object sender, BuildStartEventArgs e )
        {
            Debug.Assert( _solutionSpec.UseCKSetup );
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
            if( !_solutionSpec.NoDotNetUnitTests )
            {
                EnsureStorePath( e.Monitor, targetStore );
            }
            e.EnvironmentVariables.Add( ("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", targetStore) );
            e.EnvironmentVariables.Add( ("CKSETUP_STORE", targetStore) );
            e.EnvironmentVariables.Add( ("CKSETUP_REMOTE", targetStore) );
        }

        void OnEndBuild( object sender, EventMonitoredArgs e )
        {
            // This is an untracked file. It has to be removed, even with BuildType.IsUsingDirtyFolder
            // (except of course on the local branch).
            if( StandardPluginBranch != StandardGitStatus.Local ) Delete( e.Monitor );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            if( StandardPluginBranch == StandardGitStatus.Local
                && _solutionSpec.UseCKSetup
                && !_solutionSpec.NoDotNetUnitTests )
            {
                EnsureStorePath( m, _localFeedProvider.Local.GetCKSetupStorePath() );
            }
            else
            {
                Delete( m );
            }
        }

        public void Dispose()
        {
            if( StandardPluginBranch == StandardGitStatus.Local )
            {
                GitFolder.OnLocalBranchEntered -= OnLocalBranchEntered;
                GitFolder.OnLocalBranchLeaving -= OnLocalBranchLeaving;
            }
            if( _solutionSpec.UseCKSetup )
            {
                _solutionDriver.OnStartBuild -= OnStartBuild;
                _solutionDriver.OnEndBuild -= OnEndBuild;
            }
        }

        void OnLocalBranchLeaving( object sender, EventMonitoredArgs e ) => Delete( e.Monitor );

        void OnLocalBranchEntered( object sender, EventMonitoredArgs e )
        {
            if( _solutionSpec.UseCKSetup ) EnsureStorePath( e.Monitor, _localFeedProvider.Local.GetCKSetupStorePath() );
        }

        public bool EnsureStorePath( IActivityMonitor m, string storePath )
        {
            if( !_localFeedProvider.EnsureCKSetupStores( m ) ) return false;
            var text = "<configuration><appSettings>" + Environment.NewLine;
            text += "  -- This forces the Solutions that generate components to use the LocalFeed CKSetupStore." + Environment.NewLine;
            text += $@"  <add key=""CKSetup/DefaultStoreUrl"" value=""{storePath}"" />" + Environment.NewLine;
            text += $@"  <add key=""CKSetup/DefaultStorePath"" value=""{storePath}"" />" + Environment.NewLine;
            text += "</appSettings></configuration>";
            m.Trace( $"Updating '{FilePath}' to:{Environment.NewLine}{text}" );
            return CreateOrUpdate( m, text );
        }
    }
}
