using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Simple model that consumes the arguments of a command line.
/// </summary>
public sealed class CommandLineArguments
{
    readonly List<string> _args;
    readonly bool _hasHelp;

    /// <summary>
    /// Initializes a new <see cref="CommandLineArguments"/> from the initial process arguments.
    /// </summary>
    /// <param name="arguments">The initial arguments.</param>
    public CommandLineArguments( string[] arguments )
    {
        _args = arguments.ToList();
        _hasHelp = EatFlag( "--help", "-h", "-?" );
    }

    internal void EatPath( int pathCount )
    {
        Throw.DebugAssert( pathCount >= 0 && pathCount <= _args.Count );
        _args.RemoveRange( 0, pathCount );
    }

    /// <summary>
    /// Gets whether "--help", "-h" or "-?" was in the command line.
    /// </summary>
    public bool HasHelp => _hasHelp;

    /// <summary>
    /// Gets whether all arguments have been eaten.
    /// </summary>
    public bool IsEmpty => _args.Count == 0;

    /// <summary>
    /// Extracts the next required argument.
    /// </summary>
    /// <returns>The next agument.</returns>
    public string EatArgument()
    {
        Throw.CheckState( "There is no more expected arguments.", _args.Count > 0 );
        var s = _args[0];
        _args.RemoveAt( 0 );
        return s;
    }

    /// <summary>
    /// Extracts an option value.
    /// </summary>
    /// <param name="names">The option names.</param>
    /// <returns>The option value or null if not found.</returns>
    public string? EatSingleOption( params IEnumerable<string> names )
    {
        foreach( var n in names )
        {
            int idx = _args.IndexOf( n );
            if( idx >= 0 && ++idx < _args.Count )
            {
                var s = _args[idx];
                _args.RemoveAt( idx );
                _args.RemoveAt( idx - 1 );
                return s;
            }
        }
        return null;
    }

    /// <summary>
    /// Extracts an option values.
    /// </summary>
    /// <param name="names"></param>
    /// <param name="names">The option names.</param>
    /// <returns>The option values or null if not found.</returns>
    public string[]? EatMultipleOption( params IEnumerable<string> names )
    {
        var s1 = EatSingleOption( names );
        if( s1 == null ) return null;

        var s2 = EatSingleOption( names );
        if( s2 == null ) return [s1];

        List<string> collector = [s1, s2];
        var more = EatSingleOption( names );
        while( more != null )
        {
            collector.Add( more );
            more = EatSingleOption( names );
        }
        return collector.ToArray();
    }

    /// <summary>
    /// Extracts a flag.
    /// </summary>
    /// <param name="names">The flag names.</param>
    /// <returns>True if the flag was specified, false otherwise.</returns>
    public bool EatFlag( params IEnumerable<string> names )
    {
        bool result = false;
        foreach( var n in names )
        {
            result |= _args.Remove( n );
        }
        return result;
    }

    /// <summary>
    /// Must be called after having eaten all the expected arguments, options and flags.
    /// The command must not be executed if this returns false.
    /// </summary>
    /// <param name="monitor">The required monitor to signal the error.</param>
    /// <returns>True if the command line is empty, false otherwise.</returns>
    public bool CheckNoRemainingArguments( IActivityMonitor monitor )
    {
        if( _args.Count > 0 )
        {
            monitor.Error( $"Unexpected arguments: '{_args.Concatenate("', '")}'." );
            return false;
        }
        return true;
    }
}
