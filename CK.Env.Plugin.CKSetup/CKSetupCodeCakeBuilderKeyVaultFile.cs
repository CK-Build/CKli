using CK.Core;
using CK.Env.CKSetup;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugin
{
    public class CKSetupCodeCakeBuilderKeyVaultFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;
        readonly ISecretKeyStore _secretStore;

        public CKSetupCodeCakeBuilderKeyVaultFile(
            GitFolder f,
            NormalizedPath branchPath,
            SolutionDriver driver,
            SolutionSpec solutionSpec,
            ISecretKeyStore secretStore )
            : base( f, branchPath, branchPath.AppendPart( "CodeCakeBuilder" ).AppendPart( "CodeCakeBuilderKeyVault.txt" ) )
        {
            _driver = driver;
            _secretStore = secretStore;
            _solutionSpec = solutionSpec;
       }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath.AppendPart( "(CKSetup)" );

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            var s = _driver.GetSolution( m );
            if( s == null ) return;

            var passPhrase = _secretStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", true );

            Dictionary<string,string> current = KeyVault.DecryptValues( TextContent, passPhrase );

            if( _solutionSpec.UseCKSetup )
            {
                var storeInfo = s.ArtifactTargets.Select( t => t.Info ).OfType<ICKSetupStoreInfo>().SingleOrDefault();
                if( storeInfo == null )
                {
                    m.Error( $"Single CKSetup Artifact target not found. Since UseCKSetup is true, one and only one CKSetup store target must be available." );
                    return;
                }
                var apiKey = _secretStore.GetSecretKey( m, storeInfo.SecretKeyName, true, $"Required to push components to {storeInfo}." );
                current["CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL"] = apiKey + '|' + storeInfo.Url;
            }

            string result = KeyVault.EncryptValuesToString( current, passPhrase );
            CreateOrUpdate( m, result );
        }

    }
}
