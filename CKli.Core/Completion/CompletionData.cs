using CKli;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Core.Completion;

/// <summary>
/// Merged completion data from intrinsic commands and a plugin manifest.
/// </summary>
public sealed class CompletionData
{
    readonly Dictionary<string, CommandEntry> _commands = new();
    readonly Dictionary<string, string> _namespaces = new();
    readonly Dictionary<string, List<OptionEntry>> _options = new();
    readonly List<OptionEntry> _globals = new();
    readonly HashSet<string> _hidden;

    /// <summary>
    /// Command paths that should not appear in completion suggestions (plumbing commands).
    /// </summary>
    static readonly HashSet<string> _defaultHidden = ["complete", "completions", "completions script"];

    public CompletionData( CommandNamespace intrinsic, ManifestData? manifest, HashSet<string>? hidden = null )
    {
        _hidden = hidden ?? _defaultHidden;
        // Load intrinsic commands
        foreach( var (path, cmd) in intrinsic.Namespace )
        {
            if( cmd == null )
            {
                intrinsic.NamespaceDescriptions.TryGetValue( path, out var desc );
                _namespaces[path] = desc ?? "";
            }
            else
            {
                _commands[path] = new CommandEntry( cmd.Description, cmd.Arguments.Length );
                var opts = new List<OptionEntry>();
                foreach( var o in cmd.Options )
                    opts.Add( new OptionEntry( [.. o.Names], o.Description, o.Multiple ? "multi" : "option" ) );
                foreach( var f in cmd.Flags )
                    opts.Add( new OptionEntry( [.. f.Names], f.Description, "flag" ) );
                if( opts.Count > 0 ) _options[path] = opts;
            }
        }
        // Always add default globals (they're intrinsic to CKli, not plugin-dependent).
        foreach( var g in CKliCommands.Globals )
        {
            _globals.Add( new OptionEntry( [.. g.Names], g.Description, g.Type ) );
        }
        // Merge manifest (plugin commands)
        if( manifest != null )
        {
            foreach( var (ns, nsDesc) in manifest.Namespaces )
            {
                if( !_namespaces.ContainsKey( ns ) )
                {
                    _namespaces[ns] = nsDesc;
                }
            }
            foreach( var (path, entry) in manifest.Commands )
            {
                _commands[path] = entry;
                var mopts = manifest.GetOptionsAndFlags( path );
                if( mopts.Count > 0 )
                {
                    if( !_options.TryGetValue( path, out var list ) )
                    {
                        list = new List<OptionEntry>();
                        _options[path] = list;
                    }
                    list.AddRange( mopts );
                }
            }
            // Manifest globals are redundant with defaults - skip to avoid duplicates.
        }
    }

    public record struct CompletionResult( string Completion, string Description, string Type );

    public List<CompletionResult> GetCompletions( ReadOnlySpan<string> tokens )
    {
        var results = new List<CompletionResult>();
        if( tokens.Length == 0 )
        {
            AddTopLevel( results, "" );
            SuggestGlobals( results, [], "" );
            return results;
        }

        // Greedy command/namespace match
        string? matchedCommand = null;
        int consumed = 0;
        string pathBuilder = "";
        for( int i = 0; i < tokens.Length; i++ )
        {
            var candidate = i == 0 ? tokens[0] : pathBuilder + " " + tokens[i];
            if( _commands.ContainsKey( candidate ) )
            {
                matchedCommand = candidate;
                consumed = i + 1;
                pathBuilder = candidate;
            }
            else if( _namespaces.ContainsKey( candidate ) )
            {
                consumed = i + 1;
                pathBuilder = candidate;
                matchedCommand = null;
            }
            else
            {
                break;
            }
        }

        var remaining = tokens[consumed..];

        // Case 1: Nothing matched - suggest top-level + globals
        if( matchedCommand == null && consumed == 0 )
        {
            AddTopLevel( results, tokens[0] );
            SuggestGlobals( results, [], tokens[0] );
            return results;
        }

        // Case 2: Namespace matched, no command - suggest children
        if( matchedCommand == null )
        {
            var prefix = pathBuilder;
            var partial = remaining.Length > 0 ? remaining[0] : "";
            AddChildren( results, prefix, partial );
            return results;
        }

        // Case 3: Command matched - check cursor context
        if( remaining.Length > 0 )
        {
            // Last token is an option awaiting a value: suppress completions (user needs to type the value).
            if( IsOptionAwaitingValue( matchedCommand, remaining[^1] ) )
                return results;
        }

        // Build set of used tokens, skipping values that follow options.
        var usedTokens = new HashSet<string>();
        for( int i = 0; i < remaining.Length; i++ )
        {
            usedTokens.Add( remaining[i] );
            // If this token is an option that takes a value, skip the next token (its value).
            if( i + 1 < remaining.Length && IsOptionAwaitingValue( matchedCommand, remaining[i] ) )
            {
                i++;
            }
        }
        // Determine the partial prefix for filtering suggestions.
        // If the last token is a known flag/option, or a value following an option, the user
        // has finished typing it - use empty partial to show all remaining options.
        var partial2 = "";
        if( remaining.Length > 0 )
        {
            var lastToken = remaining[^1];
            if( IsKnownToken( matchedCommand, lastToken ) )
            {
                // Last token is a recognized flag/option - user finished typing it.
                partial2 = "";
            }
            else if( remaining.Length >= 2 && IsOptionAwaitingValue( matchedCommand, remaining[^2] ) )
            {
                // Previous token is an option awaiting a value.
                if( lastToken.Length == 0 )
                {
                    // Empty token means user hasn't typed the value yet - suppress completions.
                    return results;
                }
                // Non-empty: value has been provided - show remaining options.
                partial2 = "";
            }
            else
            {
                // Last token is a partial prefix (user is mid-typing).
                partial2 = lastToken;
            }
        }
        SuggestOptionsAndFlags( results, matchedCommand, usedTokens, partial2 );
        SuggestGlobals( results, usedTokens, partial2 );
        return results;
    }

    bool IsKnownToken( string commandPath, string token )
    {
        if( token.Length == 0 ) return false;
        if( _options.TryGetValue( commandPath, out var opts ) )
        {
            foreach( var opt in opts )
            {
                if( opt.Names.Contains( token ) ) return true;
            }
        }
        foreach( var g in _globals )
        {
            if( g.Names.Contains( token ) ) return true;
        }
        return false;
    }

    bool IsOptionAwaitingValue( string commandPath, string token )
    {
        if( _options.TryGetValue( commandPath, out var opts ) )
        {
            foreach( var opt in opts )
            {
                if( opt.Type is "option" or "multi" && opt.Names.Contains( token ) )
                    return true;
            }
        }
        // Also check globals
        foreach( var g in _globals )
        {
            if( g.Type == "option" && g.Names.Contains( token ) )
                return true;
        }
        return false;
    }

    void AddTopLevel( List<CompletionResult> results, string partial )
    {
        var seen = new HashSet<string>();
        // Add namespaces first so they take precedence over sub-command top-words.
        foreach( var (ns, desc) in _namespaces )
        {
            var topWord = ns.Split( ' ' )[0];
            if( _hidden.Contains( topWord ) ) continue;
            if( topWord.StartsWith( partial ) && seen.Add( topWord ) )
                results.Add( new CompletionResult( topWord, desc, "namespace" ) );
        }
        foreach( var (path, entry) in _commands )
        {
            var topWord = path.Split( ' ' )[0];
            if( _hidden.Contains( path ) ) continue;
            if( topWord.StartsWith( partial ) && seen.Add( topWord ) )
                results.Add( new CompletionResult( topWord, entry.Description, "command" ) );
        }
    }

    void AddChildren( List<CompletionResult> results, string prefix, string partial )
    {
        var seen = new HashSet<string>();
        foreach( var (path, entry) in _commands )
        {
            if( _hidden.Contains( path ) ) continue;
            if( path.StartsWith( prefix + " " ) )
            {
                var firstWord = path[(prefix.Length + 1)..].Split( ' ' )[0];
                if( firstWord.StartsWith( partial ) && seen.Add( firstWord ) )
                    results.Add( new CompletionResult( firstWord, entry.Description, "command" ) );
            }
        }
        foreach( var (ns, desc) in _namespaces )
        {
            if( _hidden.Contains( ns ) ) continue;
            if( ns.StartsWith( prefix + " " ) )
            {
                var firstWord = ns[(prefix.Length + 1)..].Split( ' ' )[0];
                if( firstWord.StartsWith( partial ) && seen.Add( firstWord ) )
                    results.Add( new CompletionResult( firstWord, desc, "namespace" ) );
            }
        }
    }

    void SuggestOptionsAndFlags( List<CompletionResult> results, string commandPath, HashSet<string> usedTokens, string partial )
    {
        if( !_options.TryGetValue( commandPath, out var opts ) ) return;
        foreach( var opt in opts )
        {
            if( opt.Type != "multi" && opt.IsUsedBy( usedTokens ) ) continue;
            foreach( var name in opt.Names )
            {
                if( name.StartsWith( partial ) )
                    results.Add( new CompletionResult( name, opt.Description, opt.Type ) );
            }
        }
    }

    void SuggestGlobals( List<CompletionResult> results, HashSet<string> usedTokens, string partial )
    {
        foreach( var g in _globals )
        {
            if( g.IsUsedBy( usedTokens ) ) continue;
            foreach( var name in g.Names )
            {
                if( name.StartsWith( partial ) )
                    results.Add( new CompletionResult( name, g.Description, "global" ) );
            }
        }
    }
}
