using CK.Core;
using System;
using System.Diagnostics;

namespace CKli.Core;

/// <summary>
/// Safe buffer, candidate to extension methods that should return true if they were able to
/// write everything required and otherwise should ensure that they left the content
/// untouched (typically by calling <see cref="Truncate(int)"/> with a saved <see cref="WrittenLength"/>)
/// and return false.
/// </summary>
[DebuggerDisplay("{Text,nq}")]
public ref struct FixedBufferWriter
{
    Span<char> _head;
    readonly Span<char> _buffer;

    public FixedBufferWriter( Span<char> buffer )
    {
        _buffer = buffer;
        _head = buffer;
    }

    /// <summary>
    /// Gets the length written so far.
    /// </summary>
    public readonly int WrittenLength => _buffer.Length - _head.Length;

    /// <summary>
    /// Gets the remaing available space.
    /// </summary>
    public readonly int RemainingLength => _head.Length;

    /// <summary>
    /// Gets the text written so far.
    /// </summary>
    public readonly ReadOnlySpan<char> Text => _buffer.Slice( 0, WrittenLength );

    /// <summary>
    /// Truncates the current text.
    /// </summary>
    /// <param name="length">Must be between 0 and <see cref="WrittenLength"/> or an <see cref="ArgumentException"/> is thrown.</param>
    public void Truncate( int length )
    {
        Throw.CheckArgument( length >= 0 && length <= WrittenLength );
        _head = _buffer.Slice( length );
    }

    /// <summary>
    /// Appends one character.
    /// </summary>
    /// <param name="c">The character to append.</param>
    /// <returns>True if the character has been written, false if no more space is available.</returns>
    public bool Append( char c )
    {
        if( !_head.IsEmpty )
        {
            _head[0] = c;
            _head = _head.Slice( 1 );
            return true;
        }
        return false;
    }

    public bool Append( ISpanFormattable formattable )
    {
        if( formattable.TryFormat( _head, out int written, default, System.Globalization.CultureInfo.InvariantCulture ) )
        {
            _head = _head.Slice( written );
            return true;
        }
        return false;
    }

    /// <summary>
    /// Appends two characters.
    /// </summary>
    /// <param name="c1">The first character to append.</param>
    /// <param name="c2">The second character to append.</param>
    /// <returns>True if the characters has been written, false if <see cref="RemainingLength"/> is lower than 2.</returns>
    public bool Append( char c1, char c2 )
    {
        if( _head.Length > 1 )
        {
            _head[0] = c1;
            _head[1] = c2;
            _head = _head.Slice( 2 );
            return true;
        }
        return false;
    }

    /// <summary>
    /// Appends three characters.
    /// </summary>
    /// <param name="c1">The first character to append.</param>
    /// <param name="c2">The second character to append.</param>
    /// <param name="c3">The third character to append.</param>
    /// <returns>True if the characters has been written, false if <see cref="RemainingLength"/> is lower than 3.</returns>
    public bool Append( char c1, char c2, char c3 )
    {
        if( _head.Length > 2 )
        {
            _head[0] = c1;
            _head[1] = c2;
            _head[2] = c3;
            _head = _head.Slice( 3 );
            return true;
        }
        return false;
    }

    /// <summary>
    /// Appends multiple characters.
    /// </summary>
    /// <param name="c">The character to append.</param>
    /// <param name="count">Repetition count.</param>
    /// <returns>True if the characters have been written, false if not enough space is available.</returns>
    public bool Append( char c, int count )
    {
        Throw.CheckArgument( count >= 0 );
        if( _head.Length > count )
        {
            _head.Slice( 0, count ).Fill( c );
            _head = _head.Slice( count );
            return true;
        }
        return false;
    }

    /// <summary>
    /// Appends the text.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <returns>True if the text has been written, false if <see cref="RemainingLength"/> is lower than the text's length.</returns>
    public bool Append( ReadOnlySpan<char> text )
    {
        if( _head.Length >= text.Length )
        {
            text.CopyTo( _head );
            _head = _head.Slice( text.Length );
            return true;
        }
        return false;
    }
}
