
using System;

namespace CK.Env.MSBuild
{
    public sealed class NumericNode : BaseNode
    {
        readonly Token _t;

        public NumericNode( Token t )
        {
            if( t == null ) throw new ArgumentNullException( nameof( t ) );
            _t = t;
        }

        public TokenType TokenType => _t.TokenType;

        public string Value => _t.Value;

        public override string ToString() => $"[{_t.TokenType}]{Value}";

    }
}
