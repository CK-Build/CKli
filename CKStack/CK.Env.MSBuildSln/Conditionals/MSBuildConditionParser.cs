// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using CK.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CK.Env.MSBuildSln
{
    public static class MSBuildConditionParser
    {
        /// <summary>
        /// Tries to parse the text. The out BaseNode can be null even on success: null is the empty node
        /// and is always true.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="b">The resulting node.</param>
        /// <returns>True on success, false otherwise.</returns>
        public static bool TryParse( IActivityMonitor m, string text, out BaseNode? b )
        {
            Throw.CheckNotNullArgument( text );
            var matcher = new ROSpanCharMatcher( text );
            b = Parse( ref matcher );
            if( matcher.HasError )
            {
                using( m.OpenError( $"Error while parsing condition: '{text}'" ) )
                {
                    m.Error( matcher.GetErrorMessage() );
                };
                return false;
            }
            return true;
        }

        public static BaseNode? Parse( ref ROSpanCharMatcher m )
        {
            var t = new Tokenizer( ref m );
            if( t.CurrentToken.TokenType == TokenType.EndOfInput ) return null;
            BaseNode? node = Expr( ref t );
            if( t.CurrentToken.TokenType != TokenType.EndOfInput )
            {
                m.AddExpectation( "EndOfInput" );
            }
            return node;
        }

        /// <summary>
        /// Expr => Term [or Term]*
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        static BaseNode? Expr( ref Tokenizer t )
        {
            BaseNode? node = HandleTerm( ref t );
            if( node == null ) return null;
            while( t.Match( TokenType.Or ) )
            {
                BaseNode? right = HandleTerm( ref t );
                if( right == null ) return null;
                node = new BinaryOperatorNode( node, TokenType.Or, right );
            }
            return node;
        }

        /// <summary>
        /// Term => Comparison [and Comparison]*
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        static BaseNode? HandleTerm( ref Tokenizer t )
        {
            BaseNode? node = HandleComparison( ref t );
            if( node == null ) return null;
            while( t.Match( TokenType.And ) )
            {
                BaseNode? right = HandleComparison( ref t );
                if( right == null ) return null;
                node = new BinaryOperatorNode( node, TokenType.And, right );
            }
            return node;
        }

        /// <summary>
        /// Comparison => Terminal ComparisonOperator Terminal
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        static BaseNode? HandleComparison( ref Tokenizer t )
        {
            BaseNode? left = HandleTerminal( ref t );
            if( left == null ) return null;
            TokenType op = t.CurrentToken.TokenType;
            if( op.IsComparisonOperator() )
            {
                t.Forward();
                BaseNode? right = HandleTerminal( ref t );
                if( right == null )
                {
                    t.Matcher.AddExpectation( "Terminal" );
                    return left;
                }
                return new BinaryOperatorNode( left, op, right );
            }
            return left;
        }

        /// <summary>
        /// Terminal => ValueTerminal | Function([ValueTerminal,...]) | (Expr) | !Terminal 
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        static BaseNode? HandleTerminal( ref Tokenizer t )
        {
            BaseNode? arg = HandleValueTerminal( ref t );
            if( arg != null ) return arg;

            Token current = t.CurrentToken;
            if( t.Match( TokenType.Function ) )
            {
                if( !t.Match( TokenType.OpenPar ) )
                {
                    t.Matcher.AddExpectation( "Opening parenthesis" );
                    return null;
                }
                var arglist = new List<BaseNode>();
                if( t.CurrentToken.TokenType != TokenType.ClosePar )
                {
                    do
                    {
                        var a = HandleValueTerminal( ref t );
                        if( a == null ) return null;
                        arglist.Add( a );
                    }
                    while( t.Match( TokenType.Comma ) );
                    if( !t.Match( TokenType.ClosePar ) )
                    {
                        t.Matcher.AddExpectation( "Closing parenthesis" );
                        return null;
                    }
                }
                return new FunctionCallNode( current.Value, arglist );
            }
            if( t.Match( TokenType.OpenPar ) )
            {
                BaseNode? child = Expr( ref t );
                if( child == null ) return null;
                if( !t.Match( TokenType.ClosePar ) )
                {
                    t.Matcher.AddExpectation( "Closing parenthesis" );
                    return null;
                }
                return child;
            }
            if( t.Match( TokenType.Not ) )
            {
                BaseNode? expr = HandleTerminal( ref t );
                if( expr == null ) return null;
                return new NotNode( expr );
            }
            t.Matcher.AddExpectation( "Token" );
            return null;
        }

        /// <summary>
        /// ValueTerminal => 'string' | $(property) | numeric
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        static BaseNode? HandleValueTerminal( ref Tokenizer t )
        {
            Token current = t.CurrentToken;
            if( t.Match( TokenType.String ) || t.Match( TokenType.Property ) )
            {
                Debug.Assert( current.Value != null );
                return new StringNode( current.Value );
            }
            if( t.Match( TokenType.Numeric ) )
            {
                return new NumericNode( current );
            }
            return null;
        }

    }
}
