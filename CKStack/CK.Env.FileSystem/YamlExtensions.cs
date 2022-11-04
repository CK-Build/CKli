using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace CK.Env
{
    public static class YamlExtensions
    {

        public static YamlMappingNode EnsureMap( this YamlMappingNode mapping, string key )
        {
            if( mapping.Children.TryGetValue( key, out var val ) ) return (YamlMappingNode)val;
            var map = new YamlMappingNode();
            mapping.Add( key, map );
            return map;
        }

        public static YamlMappingNode SetSequence( this YamlMappingNode m, string key, params YamlNode[] values )
        {
            var seq = new YamlSequenceNode();
            foreach( var value in values )
            {
                seq.Add( value );

            }
            if( m.Children.TryGetValue( key, out var val ) )
            {
                m.Children.Remove( key );
            }
            m.Add( key, seq );
            return m;
        }

        public static void Remove( this YamlMappingNode m, string key )
            => m.Children.Remove( key );
    }
}
