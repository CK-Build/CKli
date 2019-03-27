namespace CK.Env.MSBuild
{
    public enum TokenType
    {
        EndOfInput = 0,
        Comma,
        OpenPar,
        ClosePar,
        // Start of Comparison operators.
        LessThan,
        GreaterThan,
        LessOrEqualTo,
        GreaterOrEqualTo,
        EqualTo,
        NotEqualTo,
        // End of Comparison operators.
        And,
        Or,
        Not,
        Property,
        String,
        Numeric,
        Function,
    };

    public static class TokenTypeExtension
    {
        public static bool IsComparisonOperator( this TokenType @this )
        {
            return @this >= TokenType.LessThan && @this <= TokenType.NotEqualTo;
        }

        public static bool IsOrderingOperator( this TokenType @this )
        {
            return @this >= TokenType.LessThan && @this <= TokenType.GreaterOrEqualTo;
        }
    }
}
