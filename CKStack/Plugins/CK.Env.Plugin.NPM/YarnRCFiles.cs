using CK.Core;
using CK.Env.NPM;
using CK.SimpleKeyVault;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace CK.Env.Plugin.NPM
{
    public class YarnRCFiles : GitBranchPluginBase, ICommandMethodsProvider
    {
        readonly SolutionSpec _solutionSpec;
        readonly NPMProjectsDriver _driver;
        readonly SolutionDriver _solutionDriver;
        readonly SecretKeyStore _secretStore;

        public YarnRCFiles( GitRepository f, NPMProjectsDriver driver, SolutionDriver solutionDriver, SecretKeyStore secretStore, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath )
        {
            _solutionDriver = solutionDriver;
            _solutionSpec = solutionSpec;
            _driver = driver;
            _secretStore = secretStore;
            _solutionDriver.OnSolutionConfiguration += OnSolutionConfiguration;
        }

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;

            var projects = _driver.GetSimpleNPMProjects( m );
            if( projects == null ) return;

            foreach( var project in projects )
            {
                var npmrcPath = project.FullPath.AppendPart( ".npmrc" );
                var yarnRcPath = project.FullPath.AppendPart( ".yarnrc.yml" );
                (bool error, bool isUsingYarn) = NPMRCFiles.IsUsingYarn( m, project );
                if( error ) continue;
                if( isUsingYarn )
                {
                    DoApplySettings( m, npmrcPath, yarnRcPath );
                }
            }
        }

        void DoApplySettings( IActivityMonitor m, NormalizedPath npmrcPath, NormalizedPath yarnRcPath )
        {
            var s = _solutionDriver.GetSolution( m, allowInvalidSolution: true );
            if( s is null ) return;
            GitFolder.FileSystem.Delete( m, npmrcPath );
            var yamlInfo = GitFolder.FileSystem.GetFileInfo( yarnRcPath );
            var yamldoc = yamlInfo.Exists ? yamlInfo.ReadAsYaml() : new YamlMappingNode();

            if( !s.ArtifactSources.Any() )
            {
                yamldoc.Remove( "npmRegistries" );
                return;
            }

            var scopes = yamldoc.EnsureMap( "npmScopes" );
            scopes.Children.Clear();
            foreach( var p in s.ArtifactSources.OfType<INPMFeed>() )
            {
                bool isFile = p.Url.StartsWith( "file:" );
                if( isFile )
                {
                    m.Info( "Yarn does not support file repository. Skipping." );
                    continue;
                }
                var scope = scopes.EnsureMap( p.Scope.Substring( 1 ) );

                scope.Children["npmRegistryServer"] = p.Url;

                if( p.Credentials != null )
                {
                    string password = p.Credentials.IsSecretKeyName
                                       ? _secretStore.GetSecretKey( m, p.Credentials.PasswordOrSecretKeyName, false )!
                                       : p.Credentials.PasswordOrSecretKeyName!;
                    if( password == null )
                    {
                        if( p.Credentials.IsSecretKeyName )
                            m.Warn( $"Secret '{p.Credentials.PasswordOrSecretKeyName}' is not known. Configuration for feed '{p.Name}' skipped." );
                        else m.Warn( $"Empty feed password. Configuration for feed '{p.Name}' skipped." );
                        continue;
                    }
                    if( p.Url.Contains( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) )
                    {
                        password = Convert.ToBase64String( Encoding.UTF8.GetBytes( password ) );
                    }
                    scope.Children["npmAlwaysAuth"] = "true";
                    scope.Children["npmAuthIdent"] = "username:" + password;
                }
            }

            GitFolder.FileSystem.CopyTo( m, new Serializer().Serialize( yamldoc ), yarnRcPath );
        }



        void OnSolutionConfiguration( object? sender, SolutionConfigurationEventArgs e )
        {
            // These values are not build secrets. They are required by ApplySettings to configure
            // the NuGet.config file: once done, restore can be made and having these keys available
            // as environment variables will not help.
            var creds = e.Solution.ArtifactSources.OfType<INPMFeed>()
                            .Where( s => s.Credentials != null && s.Credentials.IsSecretKeyName )
                            .Select( s => s.Credentials.PasswordOrSecretKeyName );
            foreach( var c in creds )
            {
                _secretStore.DeclareSecretKey( c, current => current?.Description ?? "Needed to configure .yarnrc.yml file." );
            }
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => _driver.BranchPath.AppendPart( nameof( YarnRCFiles ) );

    }
}
