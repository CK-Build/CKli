using CK.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace CK.Env
{
    public class YamlFileBase : TextFileBase
    {
        YamlMappingNode? _firstMapping;

        public YamlFileBase( FileSystem fs, NormalizedPath filePath )
            : base( fs, filePath )
        {
        }

        protected YamlMappingNode? GetFirstMapping( IActivityMonitor m, bool autoCreate )
        {
            if( _firstMapping == null )
            {
                var input = TextContent;
                if( input == null && autoCreate ) input = String.Empty;
                if( input != null )
                {
                    _firstMapping = new Deserializer().Deserialize<YamlMappingNode>( input ) ?? new YamlMappingNode();
                }
            }
            return _firstMapping;
        }

        protected string? YamlMappingToString( IActivityMonitor m )
        {
            var mapping = GetFirstMapping( m, false );
            if( mapping != null )
            {
                var output = new StringBuilder();
                using( var w = new StringWriter( output ) )
                {
                    new Serializer().Serialize( w, mapping );
                }
                return output.ToString();
            }
            return null;
        }

        protected YamlMappingNode CreateKeyValue( string key, string value )
        {
            var kv = new YamlMappingNode();
            kv.Add( key, new YamlScalarNode( value ) );
            return kv;
        }

        protected static YamlMappingNode EnsureSequence( YamlMappingNode m, string key )
        {
            if( m[key] == null )
            {
                m.Add( key, new YamlSequenceNode() );
            }
            return m;
        }
    }
}
