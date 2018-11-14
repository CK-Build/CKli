using CK.Core;
using CK.NuGetClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.MSBuild
{
    class GlobalBuilderInfo
    {
        readonly ISecretKeyStore _keyStore;
        IReadOnlyList<(string Key, string Secret)> _requiredSecrets;
        GlobalGitStatus _gitStatus;
        WorkStatus _workStatus;

        public GlobalBuilderInfo( ISecretKeyStore keyStore )
        {
            _keyStore = keyStore;
        }

        /// <summary>
        /// Gets or sets whether push to remotes is required, forbidden or, when null, must be the user choice.
        /// </summary>
        public bool? IsRemotesRequired { get; set; }

        public IReadOnlyList<(string Key,string Secret)> EnsureRequiredSecretsAvailable( IActivityMonitor m, INuGetClient nugetClient, IEnumerable<Solution> solutions )
        {
            _requiredSecrets = solutions.SelectMany( s => s.Settings.NuGetPushFeeds.Select( info => nugetClient.FindOrCreate( info ) ) )
                                    .Distinct()
                                    .Where( feed => !String.IsNullOrWhiteSpace( feed.SecretKeyName ) )
                                    .GroupBy( feed => feed.SecretKeyName )
                                    .Select( g => ( g.Key, Secret: g.First().ResolveSecret( m ) ) )
                                    .Append( _keyStore.GetCKSetupRemoteStorePushKey( m ) )
                                    .ToList();
            if( _requiredSecrets.Any( r => r.Secret == null ) )
            {
                m.Error( "A required secret is missing." );
                return null;
            }
            return _requiredSecrets;
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
                AllowPackageDependenciesDowngrade = true;
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

        public bool RunUnitTests { get; set; }

        public bool AutoCommit { get; set; }

        public bool AllowPackageDependenciesDowngrade { get; set; }

    }

}
