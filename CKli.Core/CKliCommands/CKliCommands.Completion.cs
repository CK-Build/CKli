using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli;

public static partial class CKliCommands
{
    /// <summary>
    /// Console entry point: the suggestions are returned on <see cref="Console.Out"/>.
    /// </summary>
    /// <param name="arguments">The arguments to complete.</param>
    public static void HandleCompletion( ReadOnlySpan<string> arguments )
    {
        var o = (StreamWriter)Console.Out;
        o.AutoFlush = false;
        foreach( var s in GetCompletionSuggestions( arguments ) )
        {
            o.Write( s.Completion );
            o.Write( '\t' );
            o.Write( FirstLine( s.Description ) );
            o.Write( '\t' );
            o.WriteLine( s.Type );
        }
        o.Flush();

        static ReadOnlySpan<char> FirstLine( ReadOnlySpan<char> s )
        {
            var idx = s.IndexOfAny( '\r', '\n' );
            return idx >= 0 ? s[..idx] : s;
        }
    }

    public static IEnumerable<(string Completion, string Description, string Type)> GetCompletionSuggestions( ReadOnlySpan<string> arguments )
    {

    }

}
