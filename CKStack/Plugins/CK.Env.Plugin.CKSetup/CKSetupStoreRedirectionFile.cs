using CK.Core;

using System;
using System.Diagnostics;

namespace CK.Env.Plugin
{
    public class CKSetupStoreRedirectionFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider, IDisposable
    {
        readonly SolutionSpec _solutionSpec;
        readonly SolutionDriver _solutionDriver;
        readonly IEnvLocalFeedProvider _localFeedProvider;

        public CKSetupStoreRedirectionFile( GitRepository f,
                                            SolutionDriver solutionDriver,
                                            SolutionSpec settings,
                                            IEnvLocalFeedProvider localFeedProvider,
                                            NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( "CKSetupStore.txt" ) )
        {
            _solutionSpec = settings;
            _solutionDriver = solutionDriver;
            _localFeedProvider = localFeedProvider;
            // Always monitor branch change so that we delete the redirection file
            // on leaving local if it happens to exist.
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

            // Ensures that the local stores exist.
            if( !_localFeedProvider.EnsureCKSetupStores( e.Monitor ) ) return;

            var store = _localFeedProvider.GetCKSetupStorePath( e.Monitor, e.BuildType );
            EnsureStorePath( e.Monitor, store );
            // By setting this environment variable, CCB will not use the CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL from
            // its key vault (that contains the actual target remote): components will be pushed to the local store.
            // When using CCB, it is the "Publish" commands that do the actual job of transferring locally produced components
            // to the remote target store.
            e.EnvironmentVariables.Add( ("CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", store) );
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
            m.Info( $"CKSetupStore redirection file '{FilePath}' is: '{storePath}'." );
            return CreateOrUpdate( m, storePath );
        }
    }
}
