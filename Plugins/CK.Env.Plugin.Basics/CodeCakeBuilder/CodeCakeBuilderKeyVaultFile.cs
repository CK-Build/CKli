using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugin
{
    public class CodeCakeBuilderKeyVaultFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;
        readonly SecretKeyStore _secretStore;
        readonly SharedWorldState _sharedState;

        public CodeCakeBuilderKeyVaultFile(
            CodeCakeBuilderFolder f,
            SolutionDriver driver,
            SolutionSpec solutionSpec,
            SecretKeyStore secretStore,
            SharedWorldState sharedState,
            NormalizedPath branchPath )
            : base( f.GitFolder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilderKeyVault.txt" ) )
        {
            _f = f;
            _driver = driver;
            _secretStore = secretStore;
            _sharedState = sharedState;
            _solutionSpec = solutionSpec;
            _secretStore.DeclareSecretKey( SolutionDriver.CODECAKEBUILDER_SECRET_KEY, current => current?.Description
                                                ?? $"Allows update of CodeCakeBuilderKeyVault.txt used by CI/CD processes. This secret must be managed only by people that have access to the CI/CD processes and their configuration." );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        /// <summary>
        /// Raised before writing the build secrets to the CodeCakeBuyilder key vault.
        /// </summary>
        public event EventHandler<CodeCakeBuilderKeyVaultUpdatingArgs> Updating;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart
                                        && _secretStore.IsSecretKeyAvailable( SolutionDriver.CODECAKEBUILDER_SECRET_KEY ) == true;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var s = _driver.GetSolution( m, allowInvalidSolution: true );
            if( s == null ) return;

            if( _driver.BuildRequiredSecrets.Count == 0 )
            {
                m.Warn( "No build secrets collected for this solution. Skipping KeyVault configuration." );
                return;
            }

            var passPhrase = _secretStore.GetSecretKey( m, SolutionDriver.CODECAKEBUILDER_SECRET_KEY, true );

            // We forget the current vault: if more secrets are defined we lose them but this eases the
            // reset of the system with a new CODECAKEBUILDER_SECRET_KEY.
            var current = new Dictionary<string, string>();

            // The central CICDKeyVault is protected with the same CODECAKEBUILDER_SECRET_KEY secret.
            Dictionary<string, string> centralized = KeyVault.DecryptValues( _sharedState.CICDKeyVault, passPhrase );

            bool complete = true;
            foreach( var name in _driver.BuildRequiredSecrets.Select( x => x.SecretKeyName ) )
            {
                if( !centralized.TryGetValue( name, out var secret ) )
                {
                    m.Error( $"Missing required build secret '{name}' in central CICDKeyVault. Use the CICDKeyVaultUpdate command to add it." );
                    complete = false;
                }
                else
                {
                    current[name] = secret;
                }
            }
            if( complete )
            {
                Updating?.Invoke( this, new CodeCakeBuilderKeyVaultUpdatingArgs( m, _solutionSpec, s, current ) );
                string result = KeyVault.EncryptValuesToString( current, passPhrase );
                CreateOrUpdate( m, result );
            }
        }
    }
}
