// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using CK.Text;

namespace CK.Env.MSBuild
{
    public sealed class StringNode : BaseNode
    {
        public StringNode( string value )
        {
            StringValue = value ?? throw new ArgumentNullException( nameof( value ) );
            RequiresExpansion = value.IndexOf("$(", StringComparison.Ordinal ) >= 0;
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

        public override NumericNode AsNumeric
        {
            get
            {
                if( StringValue.Length == 0 || RequiresExpansion || AsBoolean.HasValue ) return null;
                var t = Tokenizer.TryParseNumeric( new StringMatcher( StringValue ) );
                return t != null ? new NumericNode( t ) : null;
            }
        }

        public override string ToString() => $"'{StringValue}'{(RequiresExpansion ? "*" : "")}";

    }
}
