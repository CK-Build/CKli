using CK.Core;
using CK.Env;
using CK.Env.Plugins;
using CK.Text;
using SharpYaml;
using SharpYaml.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CK.BuildSystem.Appveyor
{
    public class AppveyorFile : GitFolderTextFileBase, IGitBranchPlugin, ICommandMethodsProvider
    {
        readonly ISolutionSettings _settings;
        readonly ISecretKeyStore _secretStore;
        YamlStream _stream;
        YamlDocument _doc;
        YamlMapping _firstMapping;

        public AppveyorFile( GitFolder f, ISolutionSettings settings, ISecretKeyStore secretStore, NormalizedPath branchPath )
            : base( f, branchPath.AppendPart( "Appveyor.yml" ) )
        {
            _settings = settings;
            _secretStore = secretStore;
            BranchPath = branchPath;
        }

        public NormalizedPath BranchPath { get; }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => FilePath;

        public bool CanApplySettings => Folder.CurrentBranchName == BranchPath.LastPart;

        [CommandMethod]
        public void ApplySettings( IActivityMonitor m )
        {
            if( !this.CheckCurrentBranch( m ) ) return;
            YamlMapping firstMapping = GetFirstMapping( m, true );
            if( firstMapping == null ) return;

            YamlMapping env = FindOrCreateEnvironment( m, firstMapping );
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
            var install = new YamlSequence();
            install.Add( CreateKeyValue( "ps", "./CodeCakeBuilder/InstallCredentialProvider.ps1" ) );
            firstMapping["install"] = install;

            firstMapping["version"] = new YamlValue( "build{build}" );
            firstMapping["image"] = new YamlValue( "Visual Studio 2017" );
            firstMapping["clone_folder"] = new YamlValue( "C:\\CK-World\\" + Folder.SubPath.Path.Replace( '/', '\\' ) );
            EnsureDefaultBranches( firstMapping );
            EnsureSequence( firstMapping, "build_script", "dotnet run --project CodeCakeBuilder -nointeraction" );
            firstMapping["test"] = new YamlValue( "off" );

            CreateOrUpdate( m, YamlMappingToString( m ) );
        }

        protected YamlMapping GetFirstMapping( IActivityMonitor m, bool autoCreate )
        {
            if( _firstMapping == null )
            {
                var input = TextContent;
                if( input == null && autoCreate ) input = String.Empty;
                if( input != null )
                {
                    _stream = YamlStream.Load( new StringReader( input ) );
                    if( _stream.Count > 0 ) _doc = _stream[0];
                    else _stream.Add( (_doc = new YamlDocument()) );
                    if( _doc.Contents == null ) _doc.Contents = (_firstMapping = new YamlMapping());
                    else
                    {
                        _firstMapping = _doc.Contents as YamlMapping;
                        if( _firstMapping == null )
                        {
                            m.Error( $"Unable to parse Yaml file. Missing a first mapping object as the first document content." );
                        }
                    }
                }
            }
            return _firstMapping;
        }

        string YamlMappingToString( IActivityMonitor m )
        {
            if( GetFirstMapping( m, false ) != null )
            {
                var output = new StringBuilder();
                using( var w = new StringWriter( output ) )
                {
                    var emitter = new Emitter( w );
                    int i = 0;
                    foreach( var e in _stream.EnumerateEvents() )
                    {
                        emitter.Emit( e );
                        if( ++i == 3 )
                        {
                            // Remove meta header with %TAG...
                            output.Clear();
                        }
                    }
                }
                return output.ToString();
            }
            return null;
        }

        YamlMapping CreateKeyValue( string key, string value )
        {
            var kv = new YamlMapping();
            kv[key] = new YamlValue( value );
            return kv;
        }

        static YamlMapping FindOrCreateEnvironment( IActivityMonitor m, YamlMapping firstMapping )
        {
            YamlMapping env;
            YamlElement environment = firstMapping["environment"];
            if( environment != null )
            {
                env = environment as YamlMapping;
                if( env == null )
                {
                    m.Error( $"Unable to parse Yaml file. Expecting environment mapping but found '{environment.GetType()}'." );
                }
            }
            else firstMapping["environment"] = env = new YamlMapping();
            return env;
        }

        static void EnsureDefaultBranches( YamlMapping firstMapping )
        {
            YamlElement branches = firstMapping["branches"];
            if( branches == null )
            {
                firstMapping["branches"] = EnsureSequence( new YamlMapping(), "only", "master", "develop" );
            }
        }
        static YamlMapping EnsureSequence( YamlMapping m, string key, params string[] values )
        {
            var seq = new YamlSequence();
            foreach( var v in values ) seq.Add( new YamlValue( v ) );
            m[key] = seq;
            return m;
        }

        private void EnsureKeyValue( YamlMapping m, string key, string value )
        {
            m[key] = new YamlValue( value );
        }
    }
}
