using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace CKli.Core.Completion;

static class StringExt
{
    internal static string FirstLine( this string s )
    {
        var idx = s.AsSpan().IndexOfAny( '\r', '\n' );
        return idx >= 0 ? s[..idx] : s;
    }
}

public readonly record struct CommandEntry( string Description, int ArgCount );

public readonly record struct OptionEntry( string[] Names, string Description, string Type )
{
    public bool IsUsedBy( HashSet<string> usedTokens )
    {
        foreach( var n in Names )
        {
            if( usedTokens.Contains( n ) ) return true;
        }
        return false;
    }
}


/// <summary>
/// Reads and writes the completion manifest TSV file.
/// </summary>
public static class CompletionManifest
{
    /// <summary>
    /// Generates the TSV manifest string from a <see cref="CommandNamespace"/> and global entries.
    /// </summary>
    public static string Write( CommandNamespace commands, ImmutableArray<CKliCommands.GlobalDef> globals )
    {
        var sb = new StringBuilder();
        // Global section
        sb.AppendLine( "# Global" );
        foreach( var g in globals )
        {
            sb.Append( string.Join( '|', g.Names ) ).Append( '\t' ).Append( g.Description ).Append( '\t' ).AppendLine( g.Type );
        }

        // Namespaces
        sb.AppendLine( "# Namespaces" );
        foreach( var (path, cmd) in commands.Namespace )
        {
            if( cmd == null )
            {
                sb.Append( path );
                if( commands.NamespaceDescriptions.TryGetValue( path, out var desc ) && desc.Length > 0 )
                {
                    sb.Append( '\t' ).Append( desc );
                }
                sb.AppendLine();
            }
        }

        // Commands
        sb.AppendLine( "# Commands" );
        foreach( var (path, cmd) in commands.Namespace )
        {
            if( cmd != null )
            {
                sb.Append( path ).Append( '\t' )
                  .Append( cmd.Description.FirstLine() ).Append( '\t' )
                  .Append( cmd.Arguments.Length )
                  .AppendLine();
            }
        }

        // Options & Flags
        sb.AppendLine( "# Options" );
        foreach( var (path, cmd) in commands.Namespace )
        {
            if( cmd == null ) continue;
            foreach( var opt in cmd.Options )
            {
                sb.Append( path ).Append( '\t' )
                  .Append( string.Join( '|', opt.Names ) ).Append( '\t' )
                  .Append( opt.Description.FirstLine() ).Append( '\t' )
                  .AppendLine( opt.Multiple ? "multi" : "option" );
            }
            foreach( var flag in cmd.Flags )
            {
                sb.Append( path ).Append( '\t' )
                  .Append( string.Join( '|', flag.Names ) ).Append( '\t' )
                  .Append( flag.Description.FirstLine() ).Append( '\t' )
                  .AppendLine( "flag" );
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses a TSV manifest string into a <see cref="ManifestData"/>.
    /// Returns an empty manifest on null, empty, or malformed input.
    /// </summary>
    public static ManifestData Read( string? tsv )
    {
        var data = new ManifestData();
        if( string.IsNullOrEmpty( tsv ) ) return data;

        string section = "";
        foreach( var line in tsv.AsSpan().EnumerateLines() )
        {
            if( line.IsEmpty || line.IsWhiteSpace() ) continue;
            if( line.StartsWith( "#" ) )
            {
                section = line.ToString().Trim();
                continue;
            }
            var s = line.ToString();
            var parts = s.Split( '\t' );
            switch( section )
            {
                case "# Global" when parts.Length >= 3:
                    data.Globals.Add( new OptionEntry( parts[0].Split( '|' ), parts.Length > 1 ? parts[1] : "", parts[2] ) );
                    break;
                case "# Namespaces":
                    var nsParts = s.Split( '\t' );
                    data.Namespaces[nsParts[0]] = nsParts.Length > 1 ? nsParts[1] : "";
                    break;
                case "# Commands" when parts.Length >= 3 && int.TryParse( parts[2], out var argCount ):
                    data.Commands[parts[0]] = new CommandEntry( parts[1], argCount );
                    break;
                case "# Options" when parts.Length >= 4:
                    data.AddOption( parts[0], new OptionEntry( parts[1].Split( '|' ), parts[2], parts[3] ) );
                    break;
            }
        }
        return data;
    }
}
