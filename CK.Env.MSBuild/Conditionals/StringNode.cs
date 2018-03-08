// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Globalization;
using System.IO;
using System;

namespace CK.Env.MSBuild
{
    public sealed class StringNode : BaseNode
    {
        public StringNode( string value )
        {
            if( value == null ) throw new ArgumentNullException( nameof( value ) );
            Value = value;
            RequiresExpansion = value.IndexOf("$(", StringComparison.Ordinal ) >= 0;
        }

        public string Value { get; }

        public bool RequiresExpansion { get; }

        public override string ToString() => $"'{Value}'{(RequiresExpansion ? "*" : "")}";

    }
}
