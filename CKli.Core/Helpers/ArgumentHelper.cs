// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Stolen from https://github.com/dotnet/sdk/blob/main/src/Cli/Microsoft.DotNet.Cli.Utils/ArgumentEscaper.cs
//
using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CKli.Core;

/// <summary>
/// Helper for command line and process arguments.
/// </summary>
public static class ArgumentHelper
{
    /// <summary>
    /// This is hell! See https://stackoverflow.com/questions/5510343/escape-command-line-arguments-in-c-sharp
    /// <para>
    /// This implements https://learn.microsoft.com/en-us/cpp/c-language/parsing-c-command-line-arguments.
    /// </para>
    /// </summary>
    /// <param name="commandLine">The command line.</param>
    /// <returns>The individual arguments.</returns>
    public static List<string> SplitCommandLine( string commandLine )
    {
        var result = new List<string>();
        StringBuilder bArg = new StringBuilder();
        bool inQuotedString = false;
        var r = new Reader( commandLine.AsSpan().Trim() );
        while( r.Forward() )
        {
            if( r.Head == '"' )
            {
                OnStringDelimiter( bArg, ref inQuotedString, ref r );
            }
            else if( r.Head == '\\' )
            {
                // - Backslashes are interpreted literally, unless they immediately precede a double quote mark.
                // - If an even number of backslashes is followed by a double quote mark, then one backslash (\) is
                //   placed in the argv array for every pair of backslashes (\\), and the double quote mark (")
                //   is interpreted as a string delimiter.
                // - If an odd number of backslashes is followed by a double quote mark, then one backslash(\) is placed
                //   in the argv array for every pair of backslashes (\\). The double quote mark is interpreted as an escape
                //   sequence by the remaining backslash, causing a literal double quote mark (") to be placed in argv.
                //
                int count = 1;
                while( r.Next == '\\' )
                {
                    count++;
                    if( !r.Forward() ) break;
                }
                if( r.Next != '"' )
                {
                    bArg.Append( '\\', count );
                }
                else
                {
                    // The next char is the double quote (").
                    r.Forward();
                    bArg.Append( '\\', count >> 1 );
                    if( (count & 1) == 0 )
                    {
                        // Even. 
                        OnStringDelimiter( bArg, ref inQuotedString, ref r );
                    }
                    else
                    {
                        // Odd.
                        bArg.Append( '"' );
                    }
                }
            }
            else if( char.IsWhiteSpace( r.Head ) )
            {
                if( inQuotedString )
                {
                    bArg.Append( r.Head );
                }
                else
                {
                    if( bArg.Length == 0 )
                    {
                        result.Add( "\"\"" );
                    }
                    else
                    {
                        result.Add( bArg.ToString() );
                        bArg.Clear();
                    }
                    // Skip white space now: this enables the transfer of empty "" argument above.
                    while( char.IsWhiteSpace( r.Next ) && r.Forward() ) ;
                }
            }
            else bArg.Append( r.Head );
        }
        // Handle the last argument.
        if( bArg.Length > 0 )
        {
            result.Add( bArg.ToString() );
        }
        return result;

        static void OnStringDelimiter( StringBuilder bArg, ref bool inQuotedString, ref Reader r )
        {
            Throw.DebugAssert( r.Head == '"' );
            if( inQuotedString )
            {
                // "Within a quoted string, a pair of double quote marks is interpreted as a single escaped double quote mark. "
                if( r.Next == '"' )
                {
                    bArg.Append( '"' );
                    r.Forward();
                }
                else
                {
                    // End of the Quoted string.... But this is not the end of the argument: if a non whitespace
                    // char follows, this is the "A quoted string can be embedded in an argument." case.
                    inQuotedString = false;
                }
            }
            else
            {
                inQuotedString = true;
            }
        }
    }

    ref struct Reader
    {
        ReadOnlySpan<char> _input;
        int _idx;
        char _head;
        char _next;

        public Reader( ReadOnlySpan<char> input )
        {
            _input = input;
            if( _input.Length > 0 ) _next = input[0];
        }

        public char Head => _head;

        public char Next => _next;

        public bool Forward()
        {
            _head = _next;
            if( ++_idx < _input.Length )
            {
                _next = _input[_idx];
                return true;
            }
            return _idx <= _input.Length;
        }
    }


    /// <summary>
    /// Undo the processing which took place to create string[] args in Main,
    /// so that the next process will receive the same string[] args
    /// <para>
    /// See here for more info:
    /// https://docs.microsoft.com/archive/blogs/twistylittlepassagesallalike/everyone-quotes-command-line-arguments-the-wrong-way
    /// </para>
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The string with the arguments.</returns>
    public static string EscapeAndConcatenateArgArrayForProcessStart( IEnumerable<string> args )
    {
        return string.Join( " ", EscapeArgArray( args ) );
    }

    /// <summary>
    /// Undo the processing which took place to create string[] args in Main,
    /// so that the next process will receive the same string[] args
    /// <para>
    /// See here for more info:
    /// https://docs.microsoft.com/archive/blogs/twistylittlepassagesallalike/everyone-quotes-command-line-arguments-the-wrong-way
    /// </para>
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The string with the arguments.</returns>
    public static string CmdEscapeAndConcatenateArgArrayForProcessStart( IEnumerable<string> args )
    {
        return string.Join( " ", CmdEscapeArgArray( args ) );
    }

    /// <summary>
    /// Undo the processing which took place to create string[] args in Main,
    /// so that the next process will receive the same string[] args
    /// <para>
    /// See here for more info:
    /// https://docs.microsoft.com/archive/blogs/twistylittlepassagesallalike/everyone-quotes-command-line-arguments-the-wrong-way
    /// </para>
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The escaped arguments.</returns>
    static IEnumerable<string> EscapeArgArray( IEnumerable<string> args )
    {
        var escapedArgs = new List<string>();

        foreach( var arg in args )
        {
            escapedArgs.Add( EscapeSingleArg( arg ) );
        }

        return escapedArgs;
    }

    /// <summary>
    /// This prefixes every character with the '^' character to force cmd to
    /// interpret the argument string literally. An alternative option would 
    /// be to do this only for cmd metacharacters.
    /// <para>
    /// See here for more info:
    /// https://docs.microsoft.com/archive/blogs/twistylittlepassagesallalike/everyone-quotes-command-line-arguments-the-wrong-way
    /// </para>
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The escaped arguments.</returns>
    static IEnumerable<string> CmdEscapeArgArray( IEnumerable<string> arguments )
    {
        var escapedArgs = new List<string>();

        foreach( var arg in arguments )
        {
            escapedArgs.Add( CmdEscapeArg( arg ) );
        }

        return escapedArgs;
    }

    static string EscapeSingleArg( string arg, Func<string, bool>? additionalShouldSurroundWithQuotes = null )
    {
        var sb = new StringBuilder();

        var length = arg.Length;
        var needsQuotes = length == 0 || ShouldSurroundWithQuotes( arg ) || additionalShouldSurroundWithQuotes?.Invoke( arg ) == true;
        var isQuoted = needsQuotes || IsSurroundedWithQuotes( arg );

        if( needsQuotes ) sb.Append( '"' );

        for( int i = 0; i < length; ++i )
        {
            var backslashCount = 0;

            // Consume All Backslashes
            while( i < arg.Length && arg[i] == '\\' )
            {
                backslashCount++;
                i++;
            }

            // Escape any backslashes at the end of the arg
            // when the argument is also quoted.
            // This ensures the outside quote is interpreted as
            // an argument delimiter
            if( i == arg.Length && isQuoted )
            {
                sb.Append( '\\', 2 * backslashCount );
            }

            // At then end of the arg, which isn't quoted,
            // just add the backslashes, no need to escape
            else if( i == arg.Length )
            {
                sb.Append( '\\', backslashCount );
            }

            // Escape any preceding backslashes and the quote
            else if( arg[i] == '"' )
            {
                sb.Append( '\\', (2 * backslashCount) + 1 );
                sb.Append( '"' );
            }

            // Output any consumed backslashes and the character
            else
            {
                sb.Append( '\\', backslashCount );
                sb.Append( arg[i] );
            }
        }

        if( needsQuotes ) sb.Append( '"' );

        return sb.ToString();
    }

    /// <summary>
    /// Prepare as single argument to roundtrip properly through cmd.
    /// <para>
    /// This prefixes every character with the '^' character to force cmd to
    /// interpret the argument string literally. An alternative option would 
    /// be to do this only for cmd metacharacters.
    /// </para>
    /// <para>
    /// See here for more info:
    /// https://docs.microsoft.com/archive/blogs/twistylittlepassagesallalike/everyone-quotes-command-line-arguments-the-wrong-way
    /// </para>
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    static string CmdEscapeArg( string argument )
    {
        var sb = new StringBuilder();

        var quoted = ShouldSurroundWithQuotes( argument );

        if( quoted ) sb.Append( "^\"" );

        // Prepend every character with ^
        // This is harmless when passing through cmd
        // and ensures cmd metacharacters are not interpreted
        // as such
        foreach( var character in argument )
        {
            sb.Append( '^' );
            sb.Append( character );
        }

        if( quoted ) sb.Append( "^\"" );

        return sb.ToString();
    }

    // Only quote if whitespace exists in the string
    static bool ShouldSurroundWithQuotes( string argument ) => ArgumentContainsWhitespace( argument );

    static bool IsSurroundedWithQuotes( string argument ) => argument.StartsWith( '"' ) && argument.EndsWith( '"' );

    static bool ArgumentContainsWhitespace( string argument ) => argument.Contains( ' ' ) || argument.Contains( '\t' ) || argument.Contains( '\n' );
}
