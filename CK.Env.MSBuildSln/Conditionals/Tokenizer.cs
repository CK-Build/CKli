using CK.Core;
using System;

namespace CK.Env.MSBuildSln
{
    internal ref struct Tokenizer
    {
        ROSpanCharMatcher _m;
        Token _curToken;

        public Tokenizer( ref ROSpanCharMatcher matcher )
        {
            Throw.CheckArgument( matcher.HasError is false );
            _m = matcher;
            _m.SkipWhiteSpaces();
            _curToken = null!;
            DoForward();
        }

        public ROSpanCharMatcher Matcher => _m;

        public Token CurrentToken => _curToken;

        public bool Match( TokenType t )
        {
            if( _curToken.TokenType != t ) return false;
            Forward();
            return true;
        }

        public bool Forward()
        {
            if( _m.HasError ) return false;
            if( _curToken == Token.EndOfInput ) return true;
            _m.SkipWhiteSpaces();
            return DoForward();
        }

        private bool DoForward()
        {
            if( _m.Head.IsEmpty )
            {
                _curToken = Token.EndOfInput;
                return true;
            }
            switch( _m.Head[0] )
            {
                case '\'':
                    return ParseQuotedString();
                case '$':
                    return ParseProperty();
                case ',':
                    _curToken = Token.Comma;
                    _m.SafeForward( 1 );
                    return true;
                case '(':
                    _curToken = Token.LeftParenthesis;
                    _m.SafeForward( 1 );
                    return true;
                case ')':
                    _curToken = Token.RightParenthesis;
                    _m.SafeForward( 1 );
                    return true;
                case '!':
                    _m.SafeForward( 1 );
                    if( _m.Head.TryMatch( '=' ) )
                    {
                        _curToken = Token.NotEqualTo;
                        return true;
                    }
                    _curToken = Token.Not;
                    return true;
                case '%': return _m.AddExpectation( "BuiltIn or Custom %(metadata) are not supported" );
                case '@': return _m.AddExpectation( "Item @(lists) are not supported" );
                case '>':
                    _m.SafeForward( 1 );
                    if( _m.Head.TryMatch( '=' ) )
                    {
                        _curToken = Token.GreaterThanOrEqualTo;
                        return true;
                    }
                    _curToken = Token.GreaterThan;
                    return true;
                case '<':
                    _m.SafeForward( 1 );
                    if( _m.Head.TryMatch( '=' ) )
                    {
                        _curToken = Token.LessThanOrEqualTo;
                        return true;
                    }
                    _curToken = Token.LessThan;
                    return true;
                case '=':
                    _m.SafeForward( 1 );
                    if( !_m.Head.TryMatch( '=' ) ) return false;
                    _curToken = Token.EqualTo;
                    return true;
                default:
                    if( !TryParseNumeric()
                        && !TryParseIdentifierOrLogicalConnector() )
                    {
                        return _m.AddExpectation( "Token" );
                    }
                    break;
            }
            return true;
        }

        /// <summary>
        /// Handles $(propertyname).
        /// </summary>
        bool ParseProperty()
        {
            var savedHead = _m.Head;
            _m.SafeForward( 1 );
            if( !_m.TryMatch( '(' ) ) return false;
            if( !_m.Head.TryMatchTo( ')' ) )
            {
                return _m.AddExpectation( "Closing parenthesis" );
            }
            var len = savedHead.Length - _m.Head.Length;
            if( len == 3 ) _curToken = Token.EmptyString;
            else _curToken = new Token( TokenType.Property, savedHead.Slice( 0, len ).ToString() );
            return true;
        }

        /// <summary>
        /// A quoted string may contain  $(property), @(list), or 
        /// %(metadata) element. We allow here only properties.
        /// </summary>
        bool ParseQuotedString()
        {
            _m.SafeForward( 1 );
            var savedHead = _m.Head;
            if( !_m.Head.TryMatchTo( '\'' ) )
            {
                return _m.AddExpectation( "Closing quote" );
            }
            string s = savedHead.Slice( 0, savedHead.Length - _m.Head.Length - 1 ).ToString();
            if( s.Contains( "%(", StringComparison.Ordinal ) )
            {
                return _m.AddExpectation( "Item %(metadata) references are not supported." );
            }
            if( s.Contains( "@(", StringComparison.Ordinal ) )
            {
                return _m.AddExpectation( "Item @(lists) are not supported." );
            }
            _curToken = new Token( TokenType.String, s.Replace( "$()", String.Empty ) );
            return true;
        }

        bool TryParseNumeric()
        {
            var t = TryParseNumeric( ref _m );
            if( t != null )
            {
                _curToken = t;
                return true;
            }
            return false;
        }

        internal static Token? TryParseNumeric( ref ROSpanCharMatcher m )
        {
            var savedHead = m.Head;
            ulong uL = 0UL;
            long lV = 0L;
            double dV = 0.0;
            bool hasPlus = m.Head.TryMatch( '+' );
            bool hasMinus = !hasPlus && m.Head.TryMatch( '-' );
            bool isHex = m.TryMatch( "0x" ) && m.TryMatchHexNumber( out uL );
            if( isHex )
            {
                lV = (long)uL;
                if( hasMinus ) lV = -lV;
                dV = lV;
            }
            else if( m.TryMatchDouble( out dV ) )
            {
                if( hasMinus ) dV = -dV;
            }
            else
            {
                m.Head = savedHead;
                return null;
            }
            m.SetSuccess();
            return new Token( TokenType.Numeric, savedHead.Slice( 0, savedHead.Length - m.Head.Length ).ToString(), lV, dV );
        }

        bool TryParseIdentifierOrLogicalConnector()
        {
            var savedHead = _m.Head;
            if( _m.Head[0] == '_' || Char.IsLetter( _m.Head[0] ) )
            {
                do
                {
                    _m.Head = _m.Head.Slice( 1 );
                }
                while( !_m.Head.IsEmpty && (_m.Head[0] == '_' || Char.IsLetter( _m.Head[0] ) || Char.IsDigit( _m.Head[0] )) );
                string id = savedHead.Slice( 0, savedHead.Length - _m.Head.Length ).ToString();
                if( !_m.Head.IsEmpty )
                {
                    _m.SkipWhiteSpaces();
                    if( !_m.Head.IsEmpty && _m.Head[0] == '(' )
                    {
                        _curToken = new Token( TokenType.Function, id );
                        return _m.SetSuccess();
                    }
                }
                if( id.Equals( "or", StringComparison.OrdinalIgnoreCase ) )
                {
                    _curToken = Token.Or;
                }
                else if( id.Equals( "and", StringComparison.OrdinalIgnoreCase ) )
                {
                    _curToken = Token.And;
                }
                else
                {
                    _curToken = new Token( TokenType.String, id );
                }
                _m.SetSuccess();
                return true;
            }
            return false;
        }
    }
}
