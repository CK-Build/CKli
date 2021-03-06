using System;

namespace CK.Env.MSBuildSln
{
    public class PropertyReplacer : Visitor
    {
        readonly Func<string, string> _properties;

        public PropertyReplacer( Func<string, string> props )
        {
            _properties = props;
        }

        protected override BaseNode VisitString( StringNode node )
        {
            if( node.RequiresExpansion )
            {
                var newValue = node.StringValue;
                foreach( var p in node.EmbeddedProperties )
                {
                    var mapped = _properties( p );
                    if( mapped != null ) newValue = newValue.Replace( p, mapped );
                }
                if( !ReferenceEquals( newValue, node.StringValue ) )
                {
                    node = new StringNode( newValue );
                }
            }
            return node;
        }

    }
}
