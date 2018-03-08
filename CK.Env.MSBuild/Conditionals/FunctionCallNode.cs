using System;
using System.Collections.Generic;
using System.Linq;
using CK.Text;

namespace CK.Env.MSBuild
{
    public sealed class FunctionCallNode : BaseNode
    {
        public FunctionCallNode( string functionName, IReadOnlyList<BaseNode> arguments )
        {
            if( functionName == null ) throw new ArgumentNullException( nameof( functionName ) );
            if( arguments == null ) throw new ArgumentNullException( nameof( arguments ) );
            FunctionName = functionName;
            Arguments = arguments;
        }

        public string FunctionName { get; }

        public IReadOnlyList<BaseNode> Arguments { get; }

        public override string ToString() => $"{FunctionName}({Arguments.Select( a => a.ToString() ).Concatenate()})";
    }
}
