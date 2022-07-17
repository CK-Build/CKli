using System;

namespace CK.Env.MSBuildSln
{
    public sealed class Token
    {
        internal static readonly Token Comma = new Token( TokenType.Comma, "," );
        internal static readonly Token LeftParenthesis = new Token( TokenType.OpenPar, "(" );
        internal static readonly Token RightParenthesis = new Token( TokenType.ClosePar, ")" );
        internal static readonly Token LessThan = new Token( TokenType.LessThan, "<" );
        internal static readonly Token GreaterThan = new Token( TokenType.GreaterThan, ">" );
        internal static readonly Token LessThanOrEqualTo = new Token( TokenType.LessOrEqualTo, "<=" );
        internal static readonly Token GreaterThanOrEqualTo = new Token( TokenType.GreaterOrEqualTo, ">=" );
        internal static readonly Token And = new Token( TokenType.And, "and" );
        internal static readonly Token Or = new Token( TokenType.Or, "or" );
        internal static readonly Token EqualTo = new Token( TokenType.EqualTo, "==" );
        internal static readonly Token NotEqualTo = new Token( TokenType.NotEqualTo, "!=" );
        internal static readonly Token Not = new Token( TokenType.Not, "!" );
        internal static readonly Token EndOfInput = new Token();
        internal static readonly Token EmptyString = new Token( TokenType.String, String.Empty );

        Token() => TokenType = TokenType.EndOfInput;

        public Token( TokenType type, string value )
            : this( type, value, 0L, 0.0 )
        {
        }

        public Token( TokenType type, string value, long lV, double dV )
        {
            TokenType = type;
            Value = value;
            DoubleValue = dV;
            LongValue = lV;
        }

        public TokenType TokenType { get; }

        /// <summary>
        /// Gets the token string. Null for <see cref="TokenType.EndOfInput"/>.
        /// </summary>
        public string? Value { get; }

        public double DoubleValue { get; }

        public long LongValue { get; }

        public override string ToString() => $"[{TokenType}]: {Value}";
    }
}
