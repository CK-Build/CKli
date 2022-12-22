using CK.Core;
using CK.Env.MSBuildSln;
using CK.SimpleKeyVault;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace CK.Env.Plugin
{
    public class AppveyorFile : YamlFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly SolutionDriver _driver;
        readonly SolutionSpec _solutionSpec;
        readonly SecretKeyStore _keyStore;
        readonly SharedWorldState _sharedState;

        /// <summary>
        /// This must be the CODECAKEBUILDER_SECRET_KEY encrypted by the Appveyor account's secret key that will
        /// run the build: https://ci.appveyor.com/tools/encrypt.
        /// </summary>
        const string APPVEYOR_ENCRYPTED_CODECAKEBUILDER_SECRET_KEY = "APPVEYOR_ENCRYPTED_CODECAKEBUILDER_SECRET_KEY";

        public AppveyorFile( GitRepository f, SolutionDriver driver, SolutionSpec settings, SecretKeyStore keyStore, SharedWorldState sharedState, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( "appveyor.yml" ) )
        {
            _driver = driver;
            _solutionSpec = settings;
            _keyStore = keyStore;
            _sharedState = sharedState;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => GitFolder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor monitor )
        {
            if( !this.CheckCurrentBranch( monitor ) ) return;
            YamlMappingNode firstMapping = GetFirstMapping( monitor, true );
            if( firstMapping == null ) return;
            var solution = _driver.GetSolution( monitor, allowInvalidSolution: true );
            if( solution == null ) return;

            // We don't use AppVeyor for private repositories.
            if( !GitFolder.IsPublic )
            {
                if( TextContent != null )
                {
                    monitor.Log( LogLevel.Info, "The project is private, so we don't use Appveyor and the Appveyor.yml is not needed." );
                    solution.Tag<SolutionFile>()?.RemoveSolutionItemFile( monitor, FilePath.RemovePrefix( BranchPath ) );
                    Delete( monitor );
                }
                return;
            }
            // Ensure the Appveyor.yml appears in the "Solution Items".
            solution.Tag<SolutionFile>()?.EnsureSolutionItemFile( monitor, FilePath.RemovePrefix( BranchPath ) );

            // We currently always use AppVeyor when the repository is public.
            YamlMappingNode env = firstMapping.EnsureMap( "environment" );
            if( env == null ) return;

            var passphrase = _keyStore.GetSecretKey( monitor, SolutionDriver.CODECAKEBUILDER_SECRET_KEY, false );
            if( passphrase != null )
            {
                var central = KeyVault.DecryptValues( _sharedState.CICDKeyVault, passphrase );
                if( central.TryGetValue( APPVEYOR_ENCRYPTED_CODECAKEBUILDER_SECRET_KEY, out var appveyorSecure ) )
                {
                    env.Children[SolutionDriver.CODECAKEBUILDER_SECRET_KEY] = CreateKeyValue( "secure", appveyorSecure );
                }
                else
                {
                    monitor.Warn( $"Update of {SolutionDriver.CODECAKEBUILDER_SECRET_KEY} encrypted secure key has been skipped: {APPVEYOR_ENCRYPTED_CODECAKEBUILDER_SECRET_KEY} key should be defined in CICDKeyVault." );
                }
            }
            else
            {
                monitor.Info( $"Update of {SolutionDriver.CODECAKEBUILDER_SECRET_KEY} encrypted secure skipped." );
            }
            // Remove obsolete environment variables definitions.
            env.Remove( "NUGET_API_KEY" );
            env.Remove( "MYGET_RELEASE_API_KEY" );
            env.Remove( "MYGET_PREVIEW_API_KEY" );
            env.Remove( "MYGET_CI_API_KEY" );
            env.Remove( "CK_DB_TEST_MASTER_CONNECTION_STRING" );
            env.Remove( "AZURE_FEED_SIGNATURE_OPENSOURCE_PAT" );
            env.Remove( "AZURE_FEED_PAT" );
            env.Remove( "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS" );
            if( _solutionSpec.SqlServer != null )
            {
                env.Children["SqlServer/MasterConnectionString"] = $"Server=(local)\\SQL{_solutionSpec.SqlServer.ToUpperInvariant()};Database=master;User ID=sa;Password=Password12!";
            }
            //
            firstMapping.Remove( "init" );
            firstMapping.Remove( "artifacts" );
            if( _solutionSpec.SqlServer != null )
            {
                firstMapping.Children["services"] = "mssql" + _solutionSpec.SqlServer.ToLowerInvariant();
            }

            if( firstMapping.Children.GetValueOrDefault( "install" ) is YamlSequenceNode inst )
            {
                if( inst.Children.RemoveWhereAndReturnsRemoved( e => e is YamlMappingNode m
                                                            && m.Children.GetValueOrDefault( "cmd" ) is YamlScalarNode v
                                                            && (v.Value?.StartsWith( "npm install -g npm@" ) ?? false) ).Any() )
                {
                    monitor.Info( "Removed npm install with a specific version (using the Appveyor's installed one)." );
                }
                if( inst.Children.Count == 0 )
                {
                    firstMapping.Remove( "install" );
                }
            }

            firstMapping.Children["version"] = "build{build}";
            firstMapping.Children["image"] = "Visual Studio 2022";
            firstMapping.Children["clone_folder"] = "C:\\CKli-World\\" + GitFolder.SubPath.Path.Replace( '/', '\\' );
            var onFinish = new YamlSequenceNode
            {
                CreateKeyValue( "ps", "'Get-ChildItem -Recurse *.log | % { Push-AppveyorArtifact $_.FullName -FileName $_.Name -DeploymentName ''Log files'' }'" ),
                CreateKeyValue( "ps", "'Get-ChildItem -Recurse **\\Tests\\**\\TestResult*.xml | % { Push-AppveyorArtifact $_.FullName -FileName $_.Name -DeploymentName ''NUnit tests result files'' }'" ),
                CreateKeyValue( "ps", "'Get-ChildItem -Recurse **\\Tests\\**\\*.trx | % { Push-AppveyorArtifact $_.FullName -FileName $_.Name -DeploymentName ''NUnit tests result files'' }'" ),
                CreateKeyValue( "ps", "'Get-ChildItem -Recurse *.ckmon | % { Push-AppveyorArtifact $_.FullName -FileName $_.Name -DeploymentName ''Log files'' }'" )
            };
            firstMapping.Children["on_finish"] = onFinish;
            EnsureDefaultBranches( firstMapping );
            firstMapping.SetSequence( "build_script", "dotnet run --project CodeCakeBuilder -nointeraction" );
            firstMapping.Children["test"] = "off";
            CreateOrUpdate( monitor, YamlMappingToString( monitor ) );
        }

        void EnsureDefaultBranches( YamlMappingNode firstMapping )
        {
            YamlNode? branches = firstMapping.Children.GetValueOrDefault( "branches" );
            if( branches == null ) firstMapping.Children["branches"] = branches = new YamlMappingNode();
            if( branches is YamlMappingNode m )
            {
                m.SetSequence( "only", GitFolder.World.MasterBranchName, GitFolder.World.DevelopBranchName );
            }
        }
    }
}
