using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CodeCakeBuilderKeyVaultFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly ICommonSolutionSpec _settings;
        readonly ISecretKeyStore _secretStore;
        readonly ArtifactCenter _artfifacts;

        public CodeCakeBuilderKeyVaultFile(
            CodeCakeBuilderFolder f,
            ICommonSolutionSpec settings,
            ISecretKeyStore secretStore,
            ArtifactCenter artifacts,
            NormalizedPath branchPath )
            : base( f.Folder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilderKeyVault.txt" ) )
        {
            _f = f;
            _settings = settings;
            _secretStore = secretStore;
            _artfifacts = artifacts;
       }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;

            var required = new Dictionary<string, string>();

            // Skips CKSetup store (it is handled below).
            var artifactTargets = _settings.ArtifactTargets.Where( t => !(t.Info is ICKSetupStoreInfo) );
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

            if( _settings.UseCKSetup )
            {
                var storeInfo = _settings.ArtifactTargets.Select( t => t.Info ).OfType<ICKSetupStoreInfo>().SingleOrDefault();
                if( storeInfo == null )
                {
                    m.Error( $"Single CKSetup Artifact target not found. Since UseCKSetup is true, one and only one CKSetup store target must be available." );
                    return;
                }
                var s = _secretStore.GetSecretKey( m, storeInfo.SecretKeyName, true, $"Required to push components to {storeInfo}." );
                required.Add( "CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", s + '|' + storeInfo.Url );
            }
            var passPhrase = _secretStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", true );
            string result = KeyVault.EncryptValuesToString( required, passPhrase );
            CreateOrUpdate( m, result );
        }

    }
}
