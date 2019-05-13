using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugin.SolutionFiles
{
    public class CodeCakeBuilderKeyVaultFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;
        readonly ISecretKeyStore _secretStore;
        readonly ArtifactCenter _artfifacts;

        public CodeCakeBuilderKeyVaultFile(
            CodeCakeBuilderFolder f,
            SolutionDriver driver,
            SolutionSpec solutionSpec,
            ISecretKeyStore secretStore,
            ArtifactCenter artifacts,
            NormalizedPath branchPath )
            : base( f.Folder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilderKeyVault.txt" ) )
        {
            _f = f;
            _driver = driver;
            _secretStore = secretStore;
            _artfifacts = artifacts;
            _solutionSpec = solutionSpec;
       }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var s = _driver.GetSolution( m );
            if( s == null ) return;

            var required = new Dictionary<string, string>();

            // Skips CKSetup store (it is handled below).
            var artifactTargets = s.ArtifactTargets.Where( t => !(t.Info is ICKSetupStoreInfo) );
            var repositorySecrets = _artfifacts.ResolveSecrets( m, artifactTargets );
            if( repositorySecrets.Any( r => r.Secret == null ) )
            {
                m.Error( "A required repository secret is missing." );
                return;
            }
            foreach( var (SecretKeyName, Secret) in repositorySecrets )
            {
                required.Add( SecretKeyName, Secret );
            }
            if( _solutionSpec.UseCKSetup )
            {
                var storeInfo = s.ArtifactTargets.Select( t => t.Info ).OfType<ICKSetupStoreInfo>().SingleOrDefault();
                if( storeInfo == null )
                {
                    m.Error( $"Single CKSetup Artifact target not found. Since UseCKSetup is true, one and only one CKSetup store target must be available." );
                    return;
                }
                var secret = _secretStore.GetSecretKey( m, storeInfo.SecretKeyName, true, $"Required to push components to {storeInfo}." );
                required.Add( "CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", secret + '|' + storeInfo.Url );
            }
            var passPhrase = _secretStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", true );
            string result = KeyVault.EncryptValuesToString( required, passPhrase );
            CreateOrUpdate( m, result );
        }

    }
}
