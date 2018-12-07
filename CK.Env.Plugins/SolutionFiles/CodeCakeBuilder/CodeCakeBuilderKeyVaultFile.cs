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
        readonly INuGetClient _nugetClient;

        public CodeCakeBuilderKeyVaultFile(
            CodeCakeBuilderFolder f,
            ISolutionSettings settings,
            ISecretKeyStore secretStore,
            INuGetClient nugetClient,
            NormalizedPath branchPath )
            : base( f.Folder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilderKeyVault.txt" ) )
        {
            _f = f;
            _settings = settings;
            _secretStore = secretStore;
            _nugetClient = nugetClient;
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
            var nuGetSecrets = _nugetClient.ResolveSecrets( m, _settings.NuGetPushFeeds );
            if( nuGetSecrets.Any( r => r.Secret == null ) )
            {
                m.Error( "A required secret is missing." );
                return;
            }
            foreach( var s in nuGetSecrets )
            {
                required.Add( s.SecretKeyName, s.Secret );
            }
            string result = KeyVault.EncryptValuesToString( required, _secretStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", true ) );
            CreateOrUpdate( m, result );
        }

    }
}
