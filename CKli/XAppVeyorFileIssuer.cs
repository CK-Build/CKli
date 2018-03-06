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
            firstMapping.Remove( new YamlValue( "install" ) );
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
            EnsureDoubleKeyValue( env, "MYGET_RELEASE_API_KEY", "secure", "BmENGV1y8uv5cLhMhwpoDLwQiLJh4C66c53+FW8FuuVfu5Lf4Ac6NvSuqO/1MbPm" );
            EnsureDoubleKeyValue( env, "MYGET_PREVIEW_API_KEY", "secure", "CfEqNYjhrGX9DyalZ4jcadzJ/x8q25GulMCbZQDRRs+XetfHn2AEP79OJXE1wSJ8" );
            EnsureDoubleKeyValue( env, "MYGET_CI_API_KEY", "secure", "z3ZFnSM3FPCaJYkLhqjZmFTCw1Wf1hmRznQ0/UfxY/5haxctmymvFhh+PTz+/eHw" );
            if( PushToRemoteStore )
            {
                EnsureDoubleKeyValue( env, "CKSETUPREMOTESTORE_PUSH_API_KEY", "secure", "ffSyq7zhajO1GUXQraZnZiZGtrPjUMGXXhlS71JUDouxx43VgzbtRfqmZM6zKTmS" );
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
