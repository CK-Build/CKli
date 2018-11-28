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
        StandardGitStatus _gitStatus;
        GlobalWorkStatus _workStatus;

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
            if( _requiredSecrets == null )
            {
                string key = _keyStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", false, "Required to execute CodeCakeBuilder." );
                if( key == null )
                {
                    m.Error( "The CODECAKEBUILDER_SECRET_KEY is missing." );
                }
                else _requiredSecrets = new[] { ("CODECAKEBUILDER_SECRET_KEY", key) };
            }
            return _requiredSecrets;
        }

        public StandardGitStatus GlobalGitStatus => _gitStatus;

        public GlobalWorkStatus WorkStatus => _workStatus;

        public void SetStatus( GlobalWorkStatus w, StandardGitStatus g )
        {
            _gitStatus = g;
            _workStatus = w;
            if( _workStatus == GlobalWorkStatus.Idle )
            {
                if( _gitStatus == StandardGitStatus.DevelopBranch )
                {
                    TargetLocal = false;
                    RunUnitTests = true;
                    AutoCommit = false;
                    IsRemotesRequired = null;
                    AllowPackageDependenciesDowngrade = false;
                }
                else if( _gitStatus == StandardGitStatus.LocalBranch )
                {
                    TargetLocal = true;
                    RunUnitTests = true;
                    AutoCommit = true;
                    IsRemotesRequired = false;
                    AllowPackageDependenciesDowngrade = false;
                }
            }
            else if( _workStatus == GlobalWorkStatus.SwitchingToDevelop )
            {
                TargetLocal = false;
                RunUnitTests = false;
                AutoCommit = false;
                IsRemotesRequired = true;
                AllowPackageDependenciesDowngrade = false;
            }
            else if( _workStatus == GlobalWorkStatus.SwitchingToLocal )
            {
                TargetLocal = true;
                RunUnitTests = false;
                AutoCommit = false;
                IsRemotesRequired = false;
                AllowPackageDependenciesDowngrade = false;
            }
            else if( _workStatus == GlobalWorkStatus.Releasing )
            {
                TargetLocal = false;
                RunUnitTests = true;
                AutoCommit = false;
                IsRemotesRequired = false;
                AllowPackageDependenciesDowngrade = true;
            }
            else if( _workStatus == GlobalWorkStatus.CancellingRelease )
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

        public bool TargetRelease => _workStatus == GlobalWorkStatus.Releasing;

        public bool RunUnitTests { get; set; }

        public bool AutoCommit { get; set; }

        public bool AllowPackageDependenciesDowngrade { get; set; }

    }

}