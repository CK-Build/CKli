
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
            Left = left ?? throw new ArgumentNullException( nameof( left ) );
            Operator = op;
            Right = right ?? throw new ArgumentNullException( nameof( right ) );
        }

        public BaseNode Left { get; }

        public TokenType Operator { get; }

        public BaseNode Right { get; }

        public override string ToString() => $"({Left} {Operator} {Right})";

    }
}
