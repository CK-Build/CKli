using System.Globalization;
using System;
using System.Diagnostics;
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
            if( m.IsEnd ) throw new ArgumentException( "StringMatcher end reached.", nameof( m ) );
            _m = m;
        }

        public StringMatcher StringMatcher => _m;

        public Token CurrentToken => _curToken;

        public bool Match( TokenType t )
        {
            if( _curToken.TokenType == t ) return Forward();
            return false;
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
                case ',':
                    _curToken = Token.Comma;
                    return _m.Forward( 1 );
                case '(':
                    _curToken = Token.LeftParenthesis;
                    return _m.Forward( 1 );
                case ')':
                    _curToken = Token.RightParenthesis;
                    return _m.Forward( 1 );
                case '%': return _m.SetError( "BuiltIn or Custom %(metadata) are not supported.", "Tokenizer" );
                case '@': return _m.SetError( "Item @(lists) are not supported.", "Tokenizer" );
                case '$':
                    _m.Forward( 1 );
                    return ParseProperty();
                case '!':
                    _m.Forward( 1 );
                    if( _m.TryMatchChar( '=' ) )
                    {
                        _curToken = Token.NotEqualTo;
                        return true;
                    }
                    _curToken = Token.Not;
                    return true;
                case '>':
                    _m.Forward( 1 );
                    if( _m.TryMatchChar( '=' ) )
                    {
                        _curToken = Token.GreaterThanOrEqualTo;
                        return true;
                    }
                    _curToken = Token.GreaterThan;
                    return true;
                case '<':
                    if( _m.TryMatchChar( '=' ) )
                    {
                        _curToken = Token.LessThanOrEqualTo;
                        return true;
                    }
                    _curToken = Token.LessThan;
                    return true;
                case '=': return _m.MatchChar( '=' );
                case '\'':
                    _m.UncheckedMove( 1 );
                    return ParseQuotedString();
                default:
                    // Simple strings, function calls, decimal numbers, hex numbers
                    if( !ParseRemaining() )
                        return false;
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
            if( !_m.MatchChar( '(' ) ) return false;
            if( !_m.TryMatchTo( ')' ) )
            {
                return _m.SetError( "Missing closing parenthesis.", "Tokenizer" );
            }
            _curToken = new Token( TokenType.Property, _m.Text.Substring( start, _m.StartIndex - start ) );
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
            _curToken = new Token( TokenType.String, s );
            return true;
        }

        bool ParseRemaining()
        {
            int start = _m.StartIndex;
            if( _m.TryMatchHexNumber( out var l )
                || _m.TryMatchDoubleValue() )
            {
                _curToken = new Token( TokenType.Numeric, _m.Text.Substring( start, _m.StartIndex - start ) );
                return true;
            }
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
                    if( !_m.IsEnd || _m.Head == '(' )
                    {
                        _curToken = new Token( TokenType.Function, _m.Text.Substring( start, end - start ) );
                        return true;
                    }
                }
                _curToken = new Token( TokenType.String, _m.Text.Substring( start, end - start ) );
                return true;
            }
            return _m.SetError( "Unexpected character.", "Tokenizer" );
        }
    }
}
