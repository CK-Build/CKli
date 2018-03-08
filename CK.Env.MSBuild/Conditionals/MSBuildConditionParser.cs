// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Collections;
using System.Xml;
using System;
using CK.Env.MSBuild;
using CK.Text;
using System.Collections.Generic;
using CK.Core;

namespace CK.Env.MSBuild
{
    public static class MSBuildConditionParser
    {
        public static bool TryParse( IActivityMonitor m, string c, out BaseNode b )
        {
            if( c == null ) throw new ArgumentNullException( nameof( c ) );
            var matcher = new StringMatcher( c );
            b = Parse( matcher );
            if( matcher.IsError )
            {
                m.Error( $"Error while parsing condition '{c}': {matcher.ErrorMessage}." );
                return false;
            }
            return true;
        }

        public static BaseNode Parse( StringMatcher m )
        {
            var t = new Tokenizer( m );
            if( !t.Forward() )
            {
                m.SetError( "Invalid token.", "Parser" );
                return null;
            }
            BaseNode node = Expr( t );
            if( t.CurrentToken.TokenType != TokenType.EndOfInput )
            {
               m.SetError( "Unexpected token.", "Parser" );
            }
            return node;
        }

        /// <summary>
        /// Expr => Term [or Term]*
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        static BaseNode Expr( Tokenizer t )
        {
            BaseNode node = HandleTerm( t );
            while( t.CurrentToken.TokenType == TokenType.Or )
            {
                BaseNode right = HandleTerm( t );
                node = new BinaryOperatorNode( node, TokenType.Or, right );
            }
            return node;
        }

        /// <summary>
        /// Term => Comparison [and Comparison]*
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        static BaseNode HandleTerm( Tokenizer t )
        {
            BaseNode node = HandleComparison( t );
            if( node == null ) return null;
            while( t.Match( TokenType.And ) )
            {
                BaseNode right = HandleComparison( t );
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
        static BaseNode HandleComparison( Tokenizer t )
        {
            BaseNode left = HandleTerminal( t );
            if( left == null ) return null;
            TokenType op = t.CurrentToken.TokenType;
            if( op.IsComparison() )
            {
                t.Forward();
                BaseNode right = HandleTerminal( t );
                if( right == null )
                {
                    t.StringMatcher.SetError( "Expected Terminal", "Parser" );
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
        static BaseNode HandleTerminal( Tokenizer t )
        {
            BaseNode arg = HandleValueTerminal( t );
            if( arg != null ) return arg;

            Token current = t.CurrentToken;
            if( t.Match( TokenType.Function ) )
            {
                if( !t.Match( TokenType.OpenPar ) )
                {
                    t.StringMatcher.SetError( "Expected opening parenthesis." );
                    return null;
                }
                var arglist = new List<BaseNode>();
                if( t.CurrentToken.TokenType != TokenType.ClosePar )
                {
                    do
                    {
                        arglist.Add( HandleValueTerminal( t ) );
                    }
                    while( t.Match( TokenType.Comma ) );
                    if( !t.Match( TokenType.ClosePar ) )
                    {
                        t.StringMatcher.SetError( "Expected closing parenthesis." );
                        return null;
                    }
                }
                return new FunctionCallNode( current.Value, arglist );
            }
            if( t.Match( TokenType.OpenPar ) )
            {
                BaseNode child = Expr( t );
                if( !t.Match( TokenType.ClosePar ) )
                {
                    t.StringMatcher.SetError( "Expected closing parenthesis." );
                    return null;
                }
                return child;
            }
            if( t.Match( TokenType.Not ) )
            {
                BaseNode expr = HandleTerminal( t );
                if( expr == null ) return null;
                return new NotNode( expr );
            }
            t.StringMatcher.SetError( "Unexpected token." );
            return null;
        }

        /// <summary>
        /// ValueTerminal => 'string' | $(property) | numeric
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        static BaseNode HandleValueTerminal( Tokenizer t )
        {
            Token current = t.CurrentToken;
            if( t.Match( TokenType.String ) || t.Match( TokenType.Property ) )
            {
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
