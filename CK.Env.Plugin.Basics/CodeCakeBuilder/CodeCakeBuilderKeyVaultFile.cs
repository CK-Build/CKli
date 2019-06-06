using CK.Core;
using CK.SimpleKeyVault;
using CK.Text;
using System.Collections.Generic;

namespace CK.Env.Plugin
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
            : base( f.GitFolder, branchPath, f.FolderPath.AppendPart( "CodeCakeBuilderKeyVault.txt" ) )
        {
            _f = f;
            _driver = driver;
            _secretStore = secretStore;
            _artfifacts = artifacts;
            _solutionSpec = solutionSpec;
       }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !_f.EnsureDirectory( m ) ) return;
            var s = _driver.GetSolution( m );
            if( s == null ) return;

            var passPhrase = _secretStore.GetSecretKey( m, "CODECAKEBUILDER_SECRET_KEY", true );

            Dictionary<string,string> current = KeyVault.DecryptValues( TextContent, passPhrase );
            var repositorySecrets = _artfifacts.ResolveSecrets( m, s.ArtifactTargets );
            foreach( var (SecretKeyName, Secret) in repositorySecrets )
            {
                if( Secret == null )
                {
                    m.Error( "A required repository secret is missing." );
                    return;
                }
                current[SecretKeyName] = Secret;
            }
            string result = KeyVault.EncryptValuesToString( current, passPhrase );
            CreateOrUpdate( m, result );
        }

    }
}
