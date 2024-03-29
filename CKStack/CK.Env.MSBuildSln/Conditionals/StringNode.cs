using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Env.MSBuildSln
{
    public sealed class StringNode : BaseNode
    {
        public StringNode( string value )
        {
            StringValue = value ?? throw new ArgumentNullException( nameof( value ) );
            RequiresExpansion = value.Contains( "$(" );
        }

        public override string StringValue { get; }

        public override bool RequiresExpansion { get; }

        public IEnumerable<string> EmbeddedProperties
        {
            get
            {
                int idx = 0;
                while( (idx = StringValue.IndexOf( "$(", idx )) >= 0 )
                {
                    int end = StringValue.IndexOf( ')', idx + 2 );
                    yield return StringValue.Substring( idx, end - idx + 1 );
                    idx = end;
                }
            }
        }

        public override bool? AsBoolean => StringValue.Equals( "true", StringComparison.OrdinalIgnoreCase )
                                              ? true
                                              : StringValue.Equals( "false", StringComparison.OrdinalIgnoreCase )
                                                  ? (bool?)false
                                                  : null;

        public override NumericNode? AsNumeric
        {
            get
            {
                if( StringValue.Length == 0 || RequiresExpansion || AsBoolean.HasValue ) return null;
                var m = new ROSpanCharMatcher( StringValue );
                var t = Tokenizer.TryParseNumeric( ref m );
                return t != null ? new NumericNode( t ) : null;
            }
        }

        public override string ToString() => $"'{StringValue}'{(RequiresExpansion ? "*" : "")}";

    }
}
