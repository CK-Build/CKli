using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    public class GlobalBuilderInfo
    {
        string _remoteStorePushApiKey;
        string _myGetApiKey;
        GlobalGitStatus _gitStatus;

        public GlobalBuilderInfo()
        {
            _myGetApiKey = "e36689df-cf38-496e-a80c-6f1c47489334";
            _remoteStorePushApiKey = @"6L~48.M9+Hdssd87632EHjbgnI736/\TYEGJHB";
        }

        /// <summary>
        /// Gets or sets whether push to remotes is required, forbidden or, when null, must be the user choice.
        /// </summary>
        public bool? IsRemotesRequired { get; set; }

        public bool EnsureRemotesAvailable( IActivityMonitor m )
        {
            if( GetMyGetApiKey( m ) == null || GetRemoteStorePushKey( m ) == null )
            {
                m.Error( "Remote info is required." );
                return false;
            }
            return true;
        }

        public GlobalGitStatus GlobalGitStatus => _gitStatus;

        public void SetGlobalGitStatus( GlobalGitStatus s )
        {
            _gitStatus = s;
            if( _gitStatus == GlobalGitStatus.DevelopBranch )
            {
                RunUnitTests = true;
                AutoCommit = false;
                IsRemotesRequired = null;
            }
            else if( _gitStatus == GlobalGitStatus.LocalBranch )
            {
                RunUnitTests = false;
                AutoCommit = false;
                IsRemotesRequired = false;
            }
            else if( _gitStatus == GlobalGitStatus.FromLocalToDevelop )
            {
                RunUnitTests = false;
                AutoCommit = false;
                IsRemotesRequired = true;
            }
            else if( _gitStatus == GlobalGitStatus.FromDevelopToLocal )
            {
                RunUnitTests = false;
                AutoCommit = false;
                IsRemotesRequired = false;
            }
            else if( _gitStatus == GlobalGitStatus.Releasing )
            {
                RunUnitTests = true;
                AutoCommit = false;
                IsRemotesRequired = false;
            }
        }

        public bool TargetLocal => _gitStatus == GlobalGitStatus.FromDevelopToLocal || _gitStatus == GlobalGitStatus.LocalBranch;

        public bool TargetDevelop => _gitStatus == GlobalGitStatus.FromLocalToDevelop || _gitStatus == GlobalGitStatus.DevelopBranch;

        public bool TargetRelease => _gitStatus == GlobalGitStatus.Releasing;

        public string RemoteStorePushApiKey => _remoteStorePushApiKey;

        public string MyGetApiKey => _myGetApiKey;

        public bool RunUnitTests { get; set; }

        public bool AutoCommit { get; set; }

        string GetMyGetApiKey( IActivityMonitor m )
        {
            if( _myGetApiKey == null )
            {
                Console.Write( "Enter MYGET_API_KEY to push packages to remote feed: " );
                _myGetApiKey = Console.ReadLine();
                if( String.IsNullOrEmpty( _myGetApiKey ) ) _myGetApiKey = null;
            }
            return _myGetApiKey;
        }

        string GetRemoteStorePushKey( IActivityMonitor m )
        {
            if( _remoteStorePushApiKey == null )
            {
                Console.Write( "Enter https://cksetup.invenietis.net/ key to push components: " );
                _remoteStorePushApiKey = Console.ReadLine();
                if( String.IsNullOrEmpty( _remoteStorePushApiKey ) ) _remoteStorePushApiKey = null;
            }
            return _remoteStorePushApiKey;
        }

    }

}
