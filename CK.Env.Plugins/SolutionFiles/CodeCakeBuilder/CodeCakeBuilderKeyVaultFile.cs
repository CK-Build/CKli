using CK.Core;
using CK.NuGetClient;
using CK.SimpleKeyVault;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env.Plugins.SolutionFiles
{
    public class CodeCakeBuilderKeyVaultFile : TextFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly CodeCakeBuilderFolder _f;
        readonly ISolutionSettings _settings;
        readonly ISecretKeyStore _secretStore;
        readonly ArtifactCenter _artfifacts;

        public CodeCakeBuilderKeyVaultFile(
            CodeCakeBuilderFolder f,
            ISolutionSettings settings,
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

            if( _settings.ProduceCKSetupComponents )
            {
                var s = _secretStore.GetSecretKey( m, "CKSETUPREMOTESTORE_PUSH_API_KEY", true, "Required to push components to https://cksetup.invenietis.net/." );
                required.Add( "CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", s + "|https://cksetup.invenietis.net/" );
            }
            var repositorySecrets = _artfifacts.ResolveSecrets( m, _settings.ArtifactRepositoryInfos );
            if( repositorySecrets.Any( r => r.Secret == null ) )
            {
                m.Error( "A required repository secret is missing." );
                return;
            }
            foreach( var s in repositorySecrets )
            {
                required.Add( s.SecretKeyName, s.Secret );
            }
            var passPhrase = _secretStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", true );
            string result = KeyVault.EncryptValuesToString( required, passPhrase );
            CreateOrUpdate( m, result );
        }

    }
}
