using CK.Core;
using CK.Env.DependencyModel;
using CK.Env.NPM;
using CK.SimpleKeyVault;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        readonly NodeSolutionDriver _driver;
        readonly SecretKeyStore _secretStore;

        public YarnRCFiles( GitRepository f, NodeSolutionDriver nodeDriver, SecretKeyStore secretStore, SolutionSpec solutionSpec, NormalizedPath branchPath )
            : base( f, branchPath )
        {
            _solutionSpec = solutionSpec;
            _driver = nodeDriver;
            _secretStore = secretStore;
            nodeDriver.SolutionDriver.OnSolutionConfiguration += OnSolutionConfiguration;
        }

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m )
                || !_driver.TryGetSolution( m, out var solution, out var nodeSolution ) ) return;

            foreach( var project in nodeSolution.RootProjects )
            {
                var npmrcPath = project.Path.AppendPart( ".yarnrc.yml" );
                if( project.UseYarn )
                {
                    DoApplySettings( m, solution, npmrcPath );
                }
                else
                {
                    GitFolder.FileSystem.Delete( m, npmrcPath );
                }
            }
        }

        void DoApplySettings( IActivityMonitor m, ISolution solution, NormalizedPath yarnRcPath )
        {
            var yamlInfo = GitFolder.FileSystem.GetFileInfo( yarnRcPath );
            var yamldoc = yamlInfo.Exists ? yamlInfo.ReadAsYaml() : new YamlMappingNode();

            if( !solution.ArtifactSources.Any() )
            {
                yamldoc.Remove( "npmRegistries" );
            }
            else
            {
                var scopes = yamldoc.EnsureMap( "npmScopes" );
                scopes.Children.Clear();
                foreach( var feed in solution.ArtifactSources.OfType<INPMFeed>() )
                {
                    bool isFile = feed.Url.StartsWith( "file:" );
                    if( isFile )
                    {
                        m.Info( "Yarn does not support file repository. Skipping." );
                        continue;
                    }
                    var scope = scopes.EnsureMap( feed.Scope.Substring( 1 ) );

                    scope.Children["npmRegistryServer"] = feed.Url;

                    if( feed.Credentials != null )
                    {
                        string password = feed.Credentials.IsSecretKeyName
                                           ? _secretStore.GetSecretKey( m, feed.Credentials.PasswordOrSecretKeyName, false )!
                                           : feed.Credentials.PasswordOrSecretKeyName!;
                        if( password == null )
                        {
                            if( feed.Credentials.IsSecretKeyName )
                                m.Warn( $"Secret '{feed.Credentials.PasswordOrSecretKeyName}' is not known. Configuration for feed '{feed.Name}' skipped." );
                            else m.Warn( $"Empty feed password. Configuration for feed '{feed.Name}' skipped." );
                            continue;
                        }
                        if( feed.Url.Contains( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) )
                        {
                            password = Convert.ToBase64String( Encoding.UTF8.GetBytes( password ) );
                        }
                        scope.Children["npmAlwaysAuth"] = "true";
                        scope.Children["npmAuthIdent"] = "username:" + password;
                    }
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
                Debug.Assert( c != null );
                _secretStore.DeclareSecretKey( c, current => current?.Description == null
                                                                ? "Needed to configure .yarnrc.yml file."
                                                                : current.Description + Environment.NewLine + "Needed to configure .yarnrc.yml file." );
            }
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => _driver.BranchPath.AppendPart( nameof( YarnRCFiles ) );

    }
}
