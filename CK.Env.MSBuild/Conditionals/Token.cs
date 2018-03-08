namespace CK.Env.MSBuild
{
    public sealed class Token
    {
        internal readonly static Token Comma = new Token( TokenType.Comma, "," );
        internal readonly static Token LeftParenthesis = new Token( TokenType.OpenPar, "(" );
        internal readonly static Token RightParenthesis = new Token( TokenType.ClosePar, ")" );
        internal readonly static Token LessThan = new Token( TokenType.LessThan, "<" );
        internal readonly static Token GreaterThan = new Token( TokenType.GreaterThan, ">" );
        internal readonly static Token LessThanOrEqualTo = new Token( TokenType.LessThanOrEqualTo, "<=" );
        internal readonly static Token GreaterThanOrEqualTo = new Token( TokenType.GreaterThanOrEqualTo, ">=" );
        internal readonly static Token And = new Token( TokenType.And, "and" );
        internal readonly static Token Or = new Token( TokenType.Or, "or" );
        internal readonly static Token EqualTo = new Token( TokenType.EqualTo, "==" );
        internal readonly static Token NotEqualTo = new Token( TokenType.NotEqualTo, "!=" );
        internal readonly static Token Not = new Token( TokenType.Not, "!" );
        internal readonly static Token EndOfInput = new Token( TokenType.EndOfInput, null );

        public Token( TokenType type, string value )
        {
            TokenType = type;
            Value = value;
        }

        public TokenType TokenType { get; }

        public string Value { get; }

        public override string ToString() => $"[{TokenType}]: {Value}";
    }
}
