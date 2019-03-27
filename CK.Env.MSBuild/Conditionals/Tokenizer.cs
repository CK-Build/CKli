using System;
using CK.Text;

namespace CK.Env.MSBuild
{
    internal sealed class Tokenizer
    {
        readonly StringMatcher _m;
        Token _curToken;

        public Tokenizer( StringMatcher m )
        {
            if( m == null ) throw new ArgumentNullException( nameof( m ) );
            if( m.IsError ) throw new ArgumentException( "StringMatcher is on error.", nameof( m ) );
            _m = m;
            _m.MatchWhiteSpaces( 0 );
        }

        public StringMatcher StringMatcher => _m;

        public Token CurrentToken => _curToken;

        public bool Match( TokenType t )
        {
            if( _curToken.TokenType != t ) return false;
            Forward();
            return true;
        }

        public bool Forward()
        {
            if( _curToken != null )
            {
                if( _m.IsError ) return false;
                if( _curToken == Token.EndOfInput ) return true;
                if( !_m.IsEnd && !_m.MatchWhiteSpaces( 0 ) ) _m.Forward( 1 );
            }
            if( _m.IsEnd )
            {
                _curToken = Token.EndOfInput;
                return true;
            }
            switch( _m.Head )
            {
                case '\'':
                    _m.UncheckedMove( 1 );
                    return ParseQuotedString();
                case ',':
                    _curToken = Token.Comma;
                    return _m.UncheckedMove( 1 );
                case '(':
                    _curToken = Token.LeftParenthesis;
                    return _m.UncheckedMove( 1 );
                case ')':
                    _curToken = Token.RightParenthesis;
                    return _m.UncheckedMove( 1 );
                case '$': return ParseProperty();
                case '!':
                    _m.UncheckedMove( 1 );
                    if( _m.TryMatchChar( '=' ) )
                    {
                        _curToken = Token.NotEqualTo;
                        return true;
                    }
                    _curToken = Token.Not;
                    return true;
                case '%': return _m.SetError( "BuiltIn or Custom %(metadata) are not supported.", "Tokenizer" );
                case '@': return _m.SetError( "Item @(lists) are not supported.", "Tokenizer" );
                case '>':
                    _m.UncheckedMove( 1 );
                    if( _m.TryMatchChar( '=' ) )
                    {
                        _curToken = Token.GreaterThanOrEqualTo;
                        return true;
                    }
                    _curToken = Token.GreaterThan;
                    return true;
                case '<':
                    _m.UncheckedMove( 1 );
                    if( _m.TryMatchChar( '=' ) )
                    {
                        _curToken = Token.LessThanOrEqualTo;
                        return true;
                    }
                    _curToken = Token.LessThan;
                    return true;
                case '=':
                    _m.UncheckedMove( 1 );
                    if( !_m.MatchChar( '=' ) ) return false;
                    _curToken = Token.EqualTo;
                    return true;
                default:
                    if( !TryParseNumeric()
                        && !TryParseIdentifierOrLogicalConnector() )
                    {
                        return _m.SetError( "Unrecognized token.", "Tokenizer" );
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
            int start = _m.StartIndex;
            _m.Forward( 1 );
            if( !_m.MatchChar( '(' ) ) return false;
            if( !_m.TryMatchTo( ')' ) )
            {
                return _m.SetError( "Missing closing parenthesis.", "Tokenizer" );
            }
            int len = _m.StartIndex - start;
            if( len == 3 ) _curToken = Token.EmptyString;
            else _curToken = new Token( TokenType.Property, _m.Text.Substring( start, len ) );
            return true;
        }

        /// <summary>
        /// A quoted string may contain  $(property), @(list), or 
        /// %(metadata) element. We allow here only properties.
        /// </summary>
        bool ParseQuotedString()
        {
            int start = _m.StartIndex;
            if( !_m.TryMatchTo( '\'' ) )
            {
                return _m.SetError( "Unterminated quoted string.", "Tokenizer" );
            }
            string s = _m.Text.Substring( start, _m.StartIndex - start - 1 );
            if( s.IndexOf( "%(" ) >= 0 )
            {
                return _m.SetError( "Item %(metadata) references are not supported.", "Tokenizer" );
            }
            if( s.IndexOf( "@(" ) >= 0 )
            {
                return _m.SetError( "Item @(lists) are not supported.", "Tokenizer" );
            }
            _curToken = new Token( TokenType.String, s.Replace( "$()", String.Empty ) );
            return true;
        }

        bool TryParseNumeric()
        {
            var t = TryParseNumeric( _m );
            if( t != null )
            {
                _curToken = t;
                return true;
            }
            return false;
        }

        internal static Token TryParseNumeric( StringMatcher m )
        {
            int start = m.StartIndex;
            ulong uL = 0UL;
            long lV = 0L;
            double dV = 0.0;
            bool hasPlus = m.TryMatchChar( '+' );
            bool hasMinus = !hasPlus && m.TryMatchChar( '-' );
            bool isHex = m.TryMatchText( "0x" ) && m.TryMatchHexNumber( out uL );
            if( isHex )
            {
                lV = (long)uL;
                if( hasMinus ) lV = -lV;
                dV = lV;
            }
            else if( m.TryMatchDoubleValue( out dV ) )
            {
                if( hasMinus ) dV = -dV;
            }
            else
            {
                if( hasMinus || hasPlus ) m.UncheckedMove( -1 );
                return null;
            }
            return new Token( TokenType.Numeric, m.Text.Substring( start, m.StartIndex - start ), lV, dV );
        }

        bool TryParseIdentifierOrLogicalConnector()
        {
            int start = _m.StartIndex;
            if( _m.Head == '_' || Char.IsLetter( _m.Head ) )
            {
                do
                {
                    _m.Forward( 1 );
                }
                while( !_m.IsEnd && (_m.Head == '_' || Char.IsLetter( _m.Head ) || Char.IsDigit( _m.Head ) ) );
                int end = _m.StartIndex;
                if( !_m.IsEnd )
                {
                    _m.MatchWhiteSpaces( 0 );
                    if( !_m.IsEnd && _m.Head == '(' )
                    {
                        _curToken = new Token( TokenType.Function, _m.Text.Substring( start, end - start ) );
                        return true;
                    }
                }
                string s = _m.Text.Substring( start, end - start );
                if( s.Equals( "or", StringComparison.OrdinalIgnoreCase ) )
                {
                    _curToken = Token.Or;
                }
                else if( s.Equals( "and", StringComparison.OrdinalIgnoreCase ) )
                {
                    _curToken = Token.And;
                }
                else _curToken = new Token( TokenType.String, s );
                return true;
            }
            return false;
        }
    }
}
