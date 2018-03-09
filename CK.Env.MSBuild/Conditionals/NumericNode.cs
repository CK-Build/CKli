
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

        public override string StringValue => _t.Value;

        public double DoubleValue => _t.DoubleValue;

        public long LongValue => _t.LongValue;

        public override NumericNode AsNumeric => this;

        public override string ToString() => $"[Numeric]{StringValue}";

    }
}
