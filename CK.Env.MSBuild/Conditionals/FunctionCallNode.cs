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
            FunctionName = functionName ?? throw new ArgumentNullException( nameof( functionName ) );
            Arguments = arguments ?? throw new ArgumentNullException( nameof( arguments ) );
        }

        public string FunctionName { get; }

        public IReadOnlyList<BaseNode> Arguments { get; }

        public override string ToString() => $"{FunctionName}({Arguments.Select( a => a.ToString() ).Concatenate()})";
    }
}
