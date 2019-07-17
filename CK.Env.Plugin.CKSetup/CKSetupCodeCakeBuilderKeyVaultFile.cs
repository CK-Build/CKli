using CK.Core;
using CK.Env.CKSetup;
using CK.SimpleKeyVault;
using CK.Text;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugin
{
    public class CKSetupCodeCakeBuilderKeyVaultFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;
        readonly SecretKeyStore _secretStore;

        public CKSetupCodeCakeBuilderKeyVaultFile(
            GitFolder f,
            NormalizedPath branchPath,
            SolutionDriver driver,
            SolutionSpec solutionSpec,
            SecretKeyStore secretStore )
            : base( f, branchPath, branchPath.AppendPart( "CodeCakeBuilder" ).AppendPart( "CodeCakeBuilderKeyVault.txt" ) )
        {
            _driver = driver;
            _secretStore = secretStore;
            _solutionSpec = solutionSpec;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath.AppendPart( "(CKSetup)" );

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart
                                        && _secretStore.IsSecretKeyAvailable( SolutionDriver.CODECAKEBUILDER_SECRET_KEY ) == true;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            var s = _driver.GetSolution( m, allowInvalidSolution: true );
            if( s == null ) return;

            var passPhrase = _secretStore.GetSecretKey( m, SolutionDriver.CODECAKEBUILDER_SECRET_KEY, true );
            Dictionary<string,string> current = KeyVault.DecryptValues( TextContent, passPhrase );
            if( _solutionSpec.UseCKSetup )
            {
                var store = s.ArtifactTargets.OfType<CKSetupStore>().SingleOrDefault();
                if( store == null )
                {
                    m.Error( $"Single CKSetup Artifact target not found. Since UseCKSetup is true, one and only one CKSetup store target must be available." );
                    return;
                }
                var apiKey = _secretStore.GetSecretKey( m, store.SecretKeyName, true );
                current["CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL"] = apiKey + '|' + store.Url;
            }

            string result = KeyVault.EncryptValuesToString( current, passPhrase );
            CreateOrUpdate( m, result );
        }

    }
}
