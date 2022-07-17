
using System;

namespace CK.Env.MSBuildSln
{
    public sealed class NumericNode : BaseNode
    {
        readonly Token _t;

        public NumericNode( Token t )
        {
            _t = t ?? throw new ArgumentNullException( nameof( t ) );
        }

        public override string StringValue => _t.Value;

        public double DoubleValue => _t.DoubleValue;

        public long LongValue => _t.LongValue;

        public override NumericNode AsNumeric => this;

        public override string ToString() => $"[Numeric]{StringValue}";

    }
}
