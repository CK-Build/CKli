using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    class GlobalBuilderInfo
    {
        readonly IPublishKeyStore _keyStore;
        string _myGetApiKey;
        string _remoteStorePushApiKey;
        GlobalGitStatus _gitStatus;
        WorkStatus _workStatus;

        public GlobalBuilderInfo( IPublishKeyStore keyStore )
        {
            _keyStore = keyStore;
        }

        /// <summary>
        /// Gets or sets whether push to remotes is required, forbidden or, when null, must be the user choice.
        /// </summary>
        public bool? IsRemotesRequired { get; set; }

        public bool EnsureRemotesAvailable( IActivityMonitor m )
        {
            _myGetApiKey = _keyStore.GetMyGetPushKey( m );
            _remoteStorePushApiKey = _keyStore.GetCKSetupRemoteStorePushKey( m );
            if( _myGetApiKey == null || _remoteStorePushApiKey == null )
            {
                m.Error( "Remote info is required." );
                return false;
            }
            return true;
        }

        public GlobalGitStatus GlobalGitStatus => _gitStatus;

        public WorkStatus WorkStatus => _workStatus;

        public void SetStatus( WorkStatus w, GlobalGitStatus g )
        {
            _gitStatus = g;
            _workStatus = w;
            if( _workStatus == WorkStatus.Idle )
            {
                if( _gitStatus == GlobalGitStatus.DevelopBranch )
                {
                    TargetLocal = false;
                    RunUnitTests = true;
                    AutoCommit = false;
                    IsRemotesRequired = null;
                    AllowPackageDependenciesDowngrade = false;
                }
                else if( _gitStatus == GlobalGitStatus.LocalBranch )
                {
                    TargetLocal = true;
                    RunUnitTests = true;
                    AutoCommit = true;
                    IsRemotesRequired = false;
                    AllowPackageDependenciesDowngrade = false;
                }
            }
            else if( _workStatus == WorkStatus.SwitchingToDevelop )
            {
                TargetLocal = false;
                RunUnitTests = false;
                AutoCommit = false;
                IsRemotesRequired = true;
                AllowPackageDependenciesDowngrade = false;
            }
            else if( _workStatus == WorkStatus.SwitchingToLocal )
            {
                TargetLocal = true;
                RunUnitTests = false;
                AutoCommit = false;
                IsRemotesRequired = false;
                AllowPackageDependenciesDowngrade = false;
            }
            else if( _workStatus == WorkStatus.Releasing )
            {
                TargetLocal = false;
                RunUnitTests = true;
                AutoCommit = false;
                IsRemotesRequired = false;
                AllowPackageDependenciesDowngrade = false;
            }
            else if( _workStatus == WorkStatus.CancellingRelease )
            {
                TargetLocal = false;
                RunUnitTests = false;
                AutoCommit = false;
                IsRemotesRequired = false;
                AllowPackageDependenciesDowngrade = true;
            }
            else throw new InvalidOperationException( nameof( WorkStatus ) );
        }

        public bool TargetLocal { get; private set; }

        public bool TargetDevelop => !TargetLocal && !TargetRelease;

        public bool TargetRelease => _workStatus == WorkStatus.Releasing;

        public string RemoteStorePushApiKey => _remoteStorePushApiKey;

        public string MyGetApiKey => _myGetApiKey;

        public bool RunUnitTests { get; set; }

        public bool AutoCommit { get; set; }

        public bool AllowPackageDependenciesDowngrade { get; set; }

    }

}
