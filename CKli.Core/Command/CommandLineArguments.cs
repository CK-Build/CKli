using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.Core;

/// <summary>
/// Simple model that consumes the arguments of a command line.
/// </summary>
public sealed class CommandLineArguments
{
    static CommandLineArguments? _empty;

    /// <summary>
    /// A singleton empty command line.
    /// </summary>
    public static CommandLineArguments Empty => _empty ??= new CommandLineArguments();

    readonly ImmutableArray<string> _initial;
    readonly List<string> _args;
    readonly bool _hasHelp;
    readonly bool _hasCKliDebug;
    readonly bool _hasVersion;
    readonly string? _explicitPath;
    readonly string? _ckliScreen;
    readonly bool _hasInteractive;
    readonly bool _expectCommand;
    string? _initialAsStringArguments;
    Command? _foundCommand;
    HashSet<string>? _optionOrFlagNames;
    ImmutableArray<(string Argument, bool Remaining)> _remainingArguments;

    CommandLineArguments()
    {
        _initial = [];
        _args = [];
        _initialAsStringArguments = string.Empty;
        _remainingArguments = [];
    }

    /// <summary>
    /// Initializes a new <see cref="CommandLineArguments"/> from an argument string.
    /// This uses <see cref="ArgumentHelper.SplitCommandLine(string)"/>.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    public CommandLineArguments( string arguments )
        : this( ArgumentHelper.SplitCommandLine( arguments ).ToArray() )
    {
    }

    /// <summary>
    /// Initializes a new <see cref="CommandLineArguments"/> from the initial process arguments.
    /// </summary>
    /// <param name="arguments">The initial arguments.</param>
    public CommandLineArguments( string[] arguments )
    {
        _initial = [..arguments];
        var args = arguments.AsSpan();


        // If there is no argument at all, help is assumed.
        if( args.Length == 0 )
        {
            _hasHelp = true;
            _args = [];
        }
        else
        {
            // --help, -?, -h or ? must be the last arguments.
            // Otherwise we consider it to be handled by the command (this enable exec command to use it).
            _hasHelp = args.IndexOfAny( ["--help", "-?", "-h", "?"] ) == args.Length - 1;
            if( _hasHelp ) args = args.Slice( 0, args.Length - 1 );
            if( args.Length > 0 )
            {
                // If --version or -v is specified, it must comes first.
                // In such case, other arguments are simply ignored.
                // This frees any command to use --version or -v.
                if( args[0] == "--version" || args[0] == "-v" )
                {
                    _hasVersion = true;
                    args = args.Slice( 0, args.Length - 1 );
                }
                else
                {
                    Throw.DebugAssert( args.Length > 0 );
                    // We want to allow:
                    //  - ckli --path <path> ...
                    //  - ckli i[nteractive] --path <path> ...
                    //  - ckli --path <path> i[nteractive] ...
                    //  - ckli i[nteractive] ...
                    // At the start of the command to allow commands to use these rather common arguments.
                    _explicitPath = StartsWithPath( ref args );
                    _hasInteractive = StartsWithInteractive( ref args );
                    if( _hasInteractive && _explicitPath == null )
                    {
                        _explicitPath = StartsWithPath( ref args );
                    }
                }
            }
            _args = [.. args];
        }

        // --ckli-debug and --ckli-screen can appear anywhere: their --ckli prefix should be enough.
        _hasCKliDebug = EatFlag( "--ckli-debug" );
        _ckliScreen = EatSingleOption( "--ckli-screen" );
        _expectCommand = _args.Count > 0;

        static string? StartsWithPath( ref Span<string> args )
        {
            if( args.Length > 0 && args[0] == "-p" || args[0] == "--path" )
            {
                args = args.Slice( 1 );
                return args.Length == 0 ? "" : args[0];
            }
            return null;
        }

        static bool StartsWithInteractive( ref Span<string> args )
        {
            if( args.Length > 0 && args[0] == "i" || args[0] == "interactive" )
            {
                args = args.Slice( 1 );
                return true;
            }
            return false;
        }

    }

    /// <summary>
    /// Gets whether "--help", "-h", "-?" or "?" appeared at the end of the command line.
    /// </summary>
    public bool HasHelp => _hasHelp;

    /// <summary>
    /// Gets whether debugger must be launched.
    /// </summary>
    public bool HasCKliDebugFlag => _hasCKliDebug;

    /// <summary>
    /// Gets whether the "--version" or "-v" flag was specified as the first argument.
    /// Other arguments are ignored.
    /// </summary>
    public bool HasVersionFlag => _hasVersion;

    /// <summary>
    /// Gets whether the command line started with "i ..." or "interactive ..." (may be after <see cref="ExplicitPathOption"/>).
    /// <para>
    /// When "i" or "interactive" appears after, this is considered an option of the command.
    /// </para>
    /// </summary>
    public bool HasInteractiveArgument => _hasInteractive;

    /// <summary>
    /// Gets the "--path" or "-p" option that must appear before or right after <see cref="HasInteractiveArgument"/>
    /// but not after other arguments.
    /// <para>
    /// When "--path" or "-p" appears after, this is considered an option of the command.
    /// </para>
    /// </summary>
    public string? ExplicitPathOption => _explicitPath;

    /// <summary>
    /// Gets whether the command line is initially not empty after having handled <see cref="HasHelp"/>,
    /// <see cref="ExplicitPathOption"/> and <see cref="HasInteractiveArgument"/>.
    /// </summary>
    public bool ExpectCommand => _expectCommand;

    /// <summary>
    /// Gets the "--ckli-screen" option if any.
    /// </summary>
    public string? ScreenOption => _ckliScreen;

    /// <summary>
    /// Gets whether all arguments have been eaten.
    /// </summary>
    public bool IsEmpty => _args.Count == 0;

    /// <summary>
    /// Gets the number of remaining arguments, options or flags.
    /// </summary>
    public int RemainingCount => _args.Count;

    /// <summary>
    /// Gets the found command.
    /// </summary>
    public Command? FoundCommand => _foundCommand;

    /// <summary>
    /// Gets the initial arguments.
    /// </summary>
    public ImmutableArray<string> InitialArguments => _initial;

    /// <summary>
    /// Gets whether <see cref="Close"/> has been called.
    /// </summary>
    public bool IsClosed => !_remainingArguments.IsDefault;

    /// <summary>
    /// Gets the <see cref="InitialArguments"/> as a string with escaped arguments.
    /// </summary>
    public string InitialAsStringArguments => _initialAsStringArguments ??= ArgumentHelper.EscapeAndConcatenateArgArrayForProcessStart( _initial );

    /// <summary>
    /// Extracts the next required argument. <see cref="RemainingCount"/> must be positive otherwise
    /// an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    /// <returns>The next argument.</returns>
    public string EatArgument()
    {
        CheckOpened();
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
        CheckOpened();
        return DoEatSingleOption( names, out _ );
    }

    string? DoEatSingleOption( IEnumerable<string> names, out int idx )
    {
        idx = -1;
        foreach( var n in names )
        {
            idx = _args.IndexOf( n );
            if( idx >= 0 && ++idx < _args.Count )
            {
                var s = _args[idx];
                _args.RemoveAt( idx );
                _args.RemoveAt( --idx );
                return s;
            }
        }
        return null;
    }

    /// <summary>
    /// Extracts a multiple optional values.
    /// </summary>
    /// <param name="names">The option names.</param>
    /// <returns>The option values or null if not found.</returns>
    public string[]? EatMultipleOption( params IEnumerable<string> names )
    {
        CheckOpened();
        var s1 = DoEatSingleOption( names, out int idx );
        if( s1 == null ) return null;
        var result = new List<string>() { s1 };
        for(; ; )
        {
            while( idx >= 0 && idx < _args.Count )
            {
                var candidate = _args[idx];
                if( IsKnownOptionOrFlagName( candidate ) )
                {
                    break;
                }
                result.Add( candidate );
                _args.RemoveAt( idx );
            }
            s1 = DoEatSingleOption( names, out idx );
            if( s1 == null ) break;
            result.Add( s1 );
        }
        return result.ToArray();
    }

    bool IsKnownOptionOrFlagName( string candidate )
    {
        if( _foundCommand != null )
        {
            // When the command is known, we filter the exact expected options and flag names:
            // an option value CAN be "--something" (or "-s"). It is up to the command handler to
            // accept or reject this input.
            _optionOrFlagNames ??= new HashSet<string>( _foundCommand.Options.SelectMany( o => o.Names )
                                                                             .Concat( _foundCommand.Flags.SelectMany( f => f.Names ) ) );
            return _optionOrFlagNames.Contains( candidate );
        }
        // We dont' know the command. Use the hyphen as a discriminator.
        // This doesn't really matter: when the command is not found, arguments analysis is not done:
        // this is mainly for coherency (and to ease test: no command needed).
        return candidate[0] == '-';
    }

    /// <summary>
    /// Extracts a flag.
    /// </summary>
    /// <param name="names">The flag names.</param>
    /// <returns>True if the flag was specified, false otherwise.</returns>
    public bool EatFlag( params IEnumerable<string> names )
    {
        CheckOpened();
        bool result = false;
        foreach( var n in names )
        {
            int idx = _args.IndexOf( n );
            if( idx >= 0 )
            {
                _args.RemoveAt( idx );
                result = true;
            }
        }
        return result;
    }

    void CheckOpened() => Throw.CheckState( !IsClosed );

    /// <summary>
    /// Gets the initial arguments with remaining ones or an empty array on success.
    /// <para>
    /// Must be called after <see cref="Close(IActivityMonitor)"/> otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// </summary>
    /// <returns></returns>
    public ImmutableArray<(string Argument, bool Remaining)> GetRemainingArguments()
    {
        Throw.CheckState( IsClosed );
        return _remainingArguments;
    }

    /// <summary>
    /// Must be called after having eaten all the expected arguments, options and flags.
    /// The command must not be executed if this returns false.
    /// <para>
    /// This can be safely called multiple time.
    /// </para>
    /// </summary>
    /// <param name="monitor">The required monitor to signal the error.</param>
    /// <returns>True if the command line is empty, false otherwise.</returns>
    public bool Close( IActivityMonitor monitor )
    {
        if( !_remainingArguments.IsDefault ) return _remainingArguments.IsEmpty;
        if( _args.Count > 0 )
        {
            monitor.Error( $"Unexpected arguments: '{_args.Concatenate("', '")}'." );
            _remainingArguments = ComputeRemainingArguments();
            return false;
        }
        _remainingArguments = [];
        return true;
    }

    /// <summary>
    /// Closes this command line and returns a string that are the remaining arguments to use
    /// to call a <see cref="ProcessRunner.RunProcess(IActivityLineEmitter, string, string, string, Dictionary{string, string}?)"/>.
    /// <para>
    /// This cannot fail (the <paramref name="arguments"/> may be empty).
    /// </para>
    /// </summary>
    /// <param name="arguments">The arguments to use to start an external process.</param>
    public void CloseWithRemainingAsProcessStartArgs( out string arguments )
    {
        arguments = ArgumentHelper.EscapeAndConcatenateArgArrayForProcessStart( _args );
        CloseAndForgetRemaingArguments();
    }

    /// <summary>
    /// Called when a command failed without having closed the arguments.
    /// This displays the command help (without remaining arguments).
    /// </summary>
    internal void CloseAndForgetRemaingArguments()
    {
        CheckOpened();
        _args.Clear();
        _remainingArguments = [];
    }

    /// <summary>
    /// Sets the found command and eats the first <paramref name="pathCount"/> arguments.
    /// </summary>
    /// <param name="found">The found command.</param>
    /// <param name="pathCount">Number or arguments to consume.</param>
    internal void SetFoundCommand( Command found, int pathCount )
    {
        Throw.DebugAssert( FoundCommand == null );
        CheckOpened();
        _args.RemoveRange( 0, pathCount );
        _foundCommand = found;
    }

    ImmutableArray<(string Argument, bool Remaining)> ComputeRemainingArguments()
    {
        var all = _initial.Select( n => (Argument: n, Remaining: false) ).ToArray();
        foreach( var a in _args )
        {
            int idx = _initial.LastIndexOf( a );
            Throw.DebugAssert( idx >= 0 );
            while( all[idx].Remaining )
            {
                idx = _initial.LastIndexOf( a, idx - 1 );
            }
            all[idx].Remaining = true;
        }
        return ImmutableCollectionsMarshal.AsImmutableArray( all );
    }

    /// <summary>
    /// Overridden to return the <see cref="InitialAsStringArguments"/>.
    /// </summary>
    /// <returns>The InitialAsStringArguments.</returns>
    public override string ToString() => InitialAsStringArguments;
}
