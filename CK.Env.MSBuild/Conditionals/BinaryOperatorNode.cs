
using System;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Binary node: left, operator and right.
    /// </summary>
    public sealed class BinaryOperatorNode : BaseNode
    {
        public BinaryOperatorNode( BaseNode left, TokenType op, BaseNode right )
        {
            if( left == null ) throw new ArgumentNullException( nameof( left ) );
            if( right == null ) throw new ArgumentNullException( nameof( right ) );
            Left = left;
            Operator = op;
            Right = right;
        }

        public BaseNode Left { get; }

        public TokenType Operator { get; }

        public BaseNode Right { get; }

        public override string ToString() => $"({Left} {Operator} {Right})";

    }
}
