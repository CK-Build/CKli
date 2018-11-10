using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Text;
using Microsoft.Extensions.FileProviders;
using SharpYaml;
using SharpYaml.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CKli
{
    [CK.Env.XName( "AppVeyorFile" )]
    public class XAppVeyorFileIssuer : XIssuer
    {
        readonly XBranch _gitBranch;

        public XAppVeyorFileIssuer(
            XBranch gitBranch,
            IssueCollector issueCollector,
            Initializer initializer )
            : base( issueCollector, initializer )
        {
            _gitBranch = gitBranch;
        }

        public string SqlServer { get; private set; }

        public bool PushToRemoteStore { get; private set; }

        protected override bool CreateIssue( IRunContextIssue builder )
        {
            NormalizedPath appveyorFile = _gitBranch.FullPath.AppendPart( "appveyor.yml" );
            IFileInfo f = _gitBranch.FileSystem.GetFileInfo( appveyorFile );
            var input = f.Exists ? f.ReadAsText() : String.Empty;
            YamlStream stream = YamlStream.Load( new StringReader( input ) );
            YamlDocument firstDoc;
            if( stream.Count > 0 ) firstDoc = stream[0];
            else stream.Add( (firstDoc = new YamlDocument()) );
            YamlMapping firstMapping;
            if( firstDoc.Contents == null ) firstDoc.Contents = (firstMapping = new YamlMapping());
            else
            {
                firstMapping = firstDoc.Contents as YamlMapping;
                if( firstMapping == null )
                {
                    builder.Monitor.Error( $"Unable to parse Yaml file. Missing a first mapping object as the first document content." );
                    return false;
                }
            }
            firstMapping.Remove( new YamlValue( "init" ) );
            EnsureKeyValue( firstMapping, "install", "ps: ./CodeCakeBuilder/InstallCredentialProvider.ps1 - AddNetfx" );
            EnsureKeyValue( firstMapping, "version", "build{build}" );
            EnsureKeyValue( firstMapping, "image", "Visual Studio 2017" );
            EnsureKeyValue( firstMapping, "clone_folder", "C:\\CK-World\\" + _gitBranch.Parent.FullPath.Path.Replace( '/', '\\' ) );
            if( SqlServer != null )
            {
                EnsureKeyValue( firstMapping, "services", "mssql"+SqlServer );
            }
            EnsureDefaultBranches( firstMapping );
            YamlMapping env = FindOrCreateEnvironment( builder.Monitor, firstMapping );
            if( env == null ) return false;

            EnsureDoubleKeyValue( env, "AZURE_FEED_PAT", "secure", "MOjOpNMfab3UseEjZW3bGL6+7uXkbmExMOLMn2Mg+61uUO0qdrSZ28DsChFgR60/Huc+D2bcJ/GXP4CB8Cb8Vg==" );
            EnsureDoubleKeyValue( env, "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", "secure", "Nz3VB+UKkzYqapOqgvXXK0wAQkcbjlc7sjrwhYTA7zZ5MdEedplC/poHzeJh2jLFi/qniofrG9Pe8qz8l/CV2mK/1F8vPXuo7csnGxeuR27I9qKPDZncV7VtCuKHDL7DRDiQKyua1ZP5ZlVLsVmNhuQ/wc7s3zvE3Kk4a7pN/qR80fs1ZTGBKxqMrNVIACkkZq8dICYk4cko/SO9DUKyw+hvA99ehzlYcq/C9PISrBnAqAEwKUaCSe9/1SaS7jEBUwAJZbwSjqKuorPeC1hgQCkCiCRlguru/7/3c8IcoGNu9k5yGdIciy7OYNcVeqXgrLsRJjRfiJb4Ch6HsL4x+A==" );

            env.Remove( "MYGET_RELEASE_API_KEY" );
            env.Remove( "MYGET_PREVIEW_API_KEY" );
            env.Remove( "MYGET_CI_API_KEY" );

            if( PushToRemoteStore )
            {
                EnsureDoubleKeyValue( env, "CKSETUP_CAKE_TARGET_STORE_APIKEY_AND_URL", "secure", "ffSyq7zhajO1GUXQraZnZiZGtrPjUMGXXhlS71JUDos5aibfGQQ0zf4BWRjM02dn3zrvVnGZBp6bZwULB/ffASa7PO3mcKcqvppnG6eLYDU=" );
            }
            if( SqlServer != null )
            {
                EnsureKeyValue( env, "SqlServer/MasterConnectionString", $"Server=(local)\\SQL{SqlServer};Database=master;User ID=sa;Password=Password12!" );
            }
            EnsureSequence( firstMapping, "build_script", "dotnet run --project CodeCakeBuilder -nointeraction" );
            EnsureKeyValue( firstMapping, "test", "off" );

            var output = new StringBuilder();
            using( var w = new StringWriter( output ) )
            {
                var emitter = new Emitter( w );
                int i = 0;
                foreach( var e in stream.EnumerateEvents() )
                {
                    emitter.Emit( e );
                    if( ++i == 3 )
                    {
                        // Remove meta header with %TAG...
                        output.Clear();
                    }
                }
            }
            var result = output.ToString().TrimStart();
            if( input != result )
            {
                builder.CreateIssue( $"AppVeyor:{_gitBranch.FullPath}", "File appveyor.yml must be updated", m =>
                {
                    return _gitBranch.FileSystem.CopyTo( m, result, appveyorFile );
                } );
            }
            else builder.Monitor.Trace( $"appveyor.yml is up-to-date." );

            return true;
        }

        YamlMapping EnsureDoubleKeyValue( YamlMapping m, string key1, string key2, string value )
        {
            var val = new YamlMapping();
            val[key2] = new YamlValue( value );
            m[key1] = val;
            return m;
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
