using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
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
        readonly ArtifactCenter _artifacts;

        public CodeCakeBuilderKeyVaultFile(
            CodeCakeBuilderFolder f,
            SolutionDriver driver,
            SolutionSpec solutionSpec,
            SecretKeyStore secretStore,
            ArtifactCenter artifacts,
            NormalizedPath branchPath )
            : base( f.GitFolder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilderKeyVault.txt" ) )
        {
            _f = f;
            _driver = driver;
            _secretStore = secretStore;
            _artifacts = artifacts;
            _solutionSpec = solutionSpec;
            _secretStore.DeclareSecretKey( SolutionDriver.CODECAKEBUILDER_SECRET_KEY, current => current?.Description
                                                ?? $"Required to update CodeCakeBuilderKeyVault.txt used by CI processes." );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart
                                        && _secretStore.IsSecretKeyAvailable( SolutionDriver.CODECAKEBUILDER_SECRET_KEY ) == true;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var s = _driver.GetSolution( m, allowInvalidSolution: true );
            if( s == null ) return;

            var passPhrase = _secretStore.GetSecretKey( m, SolutionDriver.CODECAKEBUILDER_SECRET_KEY, true );

            Dictionary<string,string> current = KeyVault.DecryptValues( TextContent, passPhrase );
            bool complete = true;
            foreach( var secret in _driver.BuildRequiredSecrets )
            {
                if( secret.Secret == null )
                {
                    m.Error( $"Missing secret {secret.SecretKeyName}." );
                    complete = false;
                }
                else current[secret.SecretKeyName] = secret.Secret;
            }
            if( complete )
            {
                string result = KeyVault.EncryptValuesToString( current, passPhrase );
                CreateOrUpdate( m, result );
            }
        }

    }
}
