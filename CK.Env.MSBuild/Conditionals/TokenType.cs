using System;
using System.Collections.Generic;
using System.Text;

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
        LessThanOrEqualTo,
        GreaterThanOrEqualTo,
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
        public static bool IsComparison( this TokenType @this )
        {
            return @this >= TokenType.LessThan && @this <= TokenType.NotEqualTo;
        }
    }
}
