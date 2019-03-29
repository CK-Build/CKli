using CK.Core;
using CK.Text;
using SharpYaml;
using SharpYaml.Model;
using System;
using System.IO;
using System.Text;

namespace CK.Env.Plugins
{
    public class YamlFilePluginBase : TextFilePluginBase
    {
        YamlStream _yamlStream;
        YamlDocument _doc;
        YamlMapping _firstMapping;
        public YamlFilePluginBase( GitFolder f, NormalizedPath branchPath, NormalizedPath filePath ) : base( f, branchPath, filePath )
        {
        }

        protected YamlMapping GetFirstMapping( IActivityMonitor m, bool autoCreate )
        {
            if( _firstMapping == null )
            {
                var input = TextContent;
                if( input == null && autoCreate ) input = String.Empty;
                if( input != null )
                {
                    _yamlStream = YamlStream.Load( new StringReader( input ) );
                    if( _yamlStream.Count > 0 ) _doc = _yamlStream[0];
                    else _yamlStream.Add( (_doc = new YamlDocument()) );
                    if( _doc.Contents == null )
                    {
                        _doc.Contents = (_firstMapping = new YamlMapping());
                    }
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

        protected string YamlMappingToString( IActivityMonitor m )
        {
            if( GetFirstMapping( m, false ) != null )
            {
                var output = new StringBuilder();
                using( var w = new StringWriter( output ) )
                {
                    var emitter = new Emitter( w );
                    int i = 0;
                    foreach( var e in _yamlStream.EnumerateEvents() )
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

        protected YamlMapping CreateKeyValue( string key, string value )
        {
            var kv = new YamlMapping
            {
                [key] = new YamlValue( value )
            };
            return kv;
        }

        protected static YamlMapping EnsureSequence( YamlMapping m, string key, params string[] values )
        {
            var seq = new YamlSequence();
            foreach( var v in values ) seq.Add( new YamlValue( v ) );
            m[key] = seq;
            return m;
        }

        protected void EnsureKeyValue( YamlMapping m, string key, string value )
        {
            m[key] = new YamlValue( value );
        }

        protected YamlMapping FindOrCreateYamlElement( IActivityMonitor m, YamlMapping mapping, string elementName )///Too similar with AppveyorFile.FindOrCreateEnvironement. We should mutualise these.
        {
            YamlMapping jobMapping;
            YamlElement job = mapping[elementName];
            if( job != null )
            {
                jobMapping = job as YamlMapping;
                if( jobMapping == null )
                {
                    m.Error( $"Unable to parse Yaml file. Expecting job mapping but found '{job.GetType()}'." );
                }
            }
            else
            {
                mapping[elementName] = jobMapping = new YamlMapping();
            }

            return jobMapping;
        }
    }
}
