using CK.Core;
using CK.Env;
using CK.Env.Plugins;
using CK.Text;
using SharpYaml.Model;

namespace CK.BuildSystem.Appveyor
{
    public class AppveyorFile : YamlFilePluginBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly ISolutionSettings _settings;
        readonly ISecretKeyStore _secretStore;


        public AppveyorFile( GitFolder f, ISolutionSettings settings, ISecretKeyStore secretStore, NormalizedPath branchPath )
            : base( f, branchPath, branchPath.AppendPart( "Appveyor.yml" ) )
        {
            _settings = settings;
            _secretStore = secretStore;
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            YamlMapping firstMapping = GetFirstMapping( m, true );
            if( firstMapping == null ) return;

            // We don't use AppVeyor for private repositories.
            if( !Folder.IsPublic ) 
            {
                if( TextContent != null )
                {
                    m.Log( LogLevel.Info, "The project is private, so we don't use Appveyor and the Appveyor.yml is not needed." );
                    Delete( m );
                }
                return;
            }
            // We currently always use AppVeyor when the repository is public.
            YamlMapping env = FindOrCreateYamlElement( m, firstMapping, "environment" );
            if( env == null ) return;
            string appveyorSecure = _secretStore.GetSecretKey( m, "APPVEYOR_ENCRYPTED_CODECAKEBUILDER_SECRET_KEY", false );
            if( appveyorSecure != null )
            {
                env["CODECAKEBUILDER_SECRET_KEY"] = CreateKeyValue( "secure", appveyorSecure );
            }
            else
            {
                m.Warn( "Update of CODECAKEBUILDER_SECRET_KEY secure key has been skipped." );
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
            if( _settings.SqlServer != null )
            {
                env["SqlServer/MasterConnectionString"] = new YamlValue( $"Server=(local)\\SQL{_settings.SqlServer.ToUpperInvariant()};Database=master;User ID=sa;Password=Password12!" );
            }
            //
            firstMapping.Remove( new YamlValue( "init" ) );
            if( _settings.SqlServer != null )
            {
                firstMapping["services"] = new YamlValue( "mssql" + _settings.SqlServer.ToLowerInvariant() );
            }
            var install = new YamlSequence
            {
                CreateKeyValue( "ps", "./CodeCakeBuilder/InstallCredentialProvider.ps1" )
            };
            firstMapping["install"] = install;

            firstMapping["version"] = new YamlValue( "build{build}" );
            firstMapping["image"] = new YamlValue( "Visual Studio 2017" );
            firstMapping["clone_folder"] = new YamlValue( "C:\\CK-World\\" + Folder.SubPath.Path.Replace( '/', '\\' ) );
            EnsureDefaultBranches( firstMapping );
            EnsureSequence( firstMapping, "build_script", "dotnet run --project CodeCakeBuilder -nointeraction" );
            firstMapping["test"] = new YamlValue( "off" );

            CreateOrUpdate( m, YamlMappingToString( m ) );
        }

        static void EnsureDefaultBranches( YamlMapping firstMapping )
        {
            YamlElement branches = firstMapping["branches"];
            if( branches == null )
            {
                firstMapping["branches"] = EnsureSequence( new YamlMapping(), "only", "master", "develop" );
            }
        }

    }
}
