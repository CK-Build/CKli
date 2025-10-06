using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CKli.Core;

sealed partial class CommandCollector
{
    readonly List<PluginCommand> _pluginCommands;
    readonly CommandNamespaceBuilder _commands;

    public List<PluginCommand> PluginCommands => _pluginCommands;

    public CommandNamespace BuildCommands() => _commands.Build();

    public CommandCollector()
    {
        _pluginCommands = new List<PluginCommand>();
        _commands = new CommandNamespaceBuilder();
    }

    public void Add( IPluginTypeInfo typeInfo, MethodInfo method, string commandPath, IList<CustomAttributeData> attributes )
    {
        commandPath = NormalizePath( commandPath );
        if( !CommandDescription.IsValidCommandPath( commandPath ) )
        {
            Throw.CKException( $"Invalid command path '{commandPath}'." );
        }
        if( CKliCommands.Commands.Find( commandPath ) != null )
        {
            Throw.CKException( $"""
                Invalid [CommandPath( "{commandPath}" )] on '{typeInfo.TypeName}.{method.Name}' method.
                This path is an intrinsic CKli command.
                """ );
        }
        var description = GetDescription( attributes );
        var parameters = method.GetParameters();
        if( parameters.Length == 0 || parameters[0].ParameterType != typeof( IActivityMonitor ) )
        {
            Throw.CKException( $"Invalid command method '{typeInfo.TypeName}.{method.Name}': the first parameter must be a 'IActivityMonitor'." );
        }

        MethodAsyncReturn retType = MethodAsyncReturn.None;
        var returnedType = method.ReturnType;
        if( returnedType != typeof( bool ) )
        {
            if( returnedType == typeof( ValueTask<bool> ) )
            {
                retType = MethodAsyncReturn.ValueTask;
            }
            else if( returnedType != typeof( Task<bool> ) )
            {
                retType = MethodAsyncReturn.Task;
            }
            else
            {
                Throw.CKException( $"Invalid command method '{typeInfo.TypeName}.{method.Name}': a command method must return a boolean, a ValueTask<bool> or a Task<bool>." );
            }
        }
        var arguments = ImmutableArray.CreateBuilder<(string Name, string Description)>();
        var options = ImmutableArray.CreateBuilder<(ImmutableArray<string> Names, string Description, bool Multiple)>();
        var flags = ImmutableArray.CreateBuilder<(ImmutableArray<string> Names, string Description)>();
        int iP;
        // Arguments: Eats strings until they become optional,
        // or until an array of strings is found,
        // or a boolean is found.
        for( iP = 1; iP < parameters.Length; iP++ )
        {
            var p = parameters[iP];
            if( p.ParameterType == typeof( string ) )
            {
                if( p.HasDefaultValue )
                {
                    break;
                }
                arguments.Add( (p.Name!, GetDescription( p.GetCustomAttributesData() )) );
            }
            else if( p.ParameterType == typeof( string[] ) )
            {
                break;
            }
            else if( ExpectBooleanFlag( typeInfo, method, p ) )
            {
                break;
            }
        }
        // Options: Eats strings and string arrays until the first boolean.
        for( ; iP < parameters.Length; iP++ )
        {
            var p = parameters[iP];
            if( p.ParameterType == typeof( string ) )
            {
                var attr = p.GetCustomAttributesData();
                options.Add( (GetOptionOrFlagNames( typeInfo, method, p, attr ), GetDescription( attr ), Multiple: false) );
            }
            else if( p.ParameterType == typeof( string[] ) )
            {
                var attr = p.GetCustomAttributesData();
                options.Add( (GetOptionOrFlagNames( typeInfo, method, p, attr ), GetDescription( attr ), Multiple: true) );
            }
            else if( ExpectBooleanFlag( typeInfo, method, p ) )
            {
                break;
            }
        }
        // Flags: Eats booleans.
        for( ;  iP < parameters.Length; iP++ )
        {
            var p = parameters[iP];
            if( ExpectBooleanFlag( typeInfo, method, p ) )
            {
                var attr = p.GetCustomAttributesData();
                flags.Add( (GetOptionOrFlagNames( typeInfo, method, p, attr ), GetDescription( attr )) );
            }
        }
        var cmd = new ReflectionPluginCommand( typeInfo,
                                               commandPath,
                                               description,
                                               arguments.DrainToImmutable(),
                                               options.DrainToImmutable(),
                                               flags.DrainToImmutable(),
                                               method,
                                               parameters.Length,
                                               retType );
        if( !_commands.TryAdd( cmd, out var exists ) )
        {
            Throw.CKException( $"""
                Duplicate [CommandPath( "{commandPath}" )] on '{cmd}' method.
                This path is already defined by '{exists}'.
                """ );
        }
        _pluginCommands.Add( cmd );


        static string GetDescription( IList<CustomAttributeData> attributes )
        {
            var result = "<no description>";
            foreach( var a in attributes )
            {
                if( a.AttributeType == typeof( DescriptionAttribute ) )
                {
                    var s = (string?)a.ConstructorArguments[0].Value;
                    if( !string.IsNullOrWhiteSpace( s ) )
                    {
                        result = s;
                    }
                    break;
                }
            }
            return result;
        }

        static ImmutableArray<string> GetOptionOrFlagNames( IPluginTypeInfo typeInfo, MethodInfo method, ParameterInfo p, IList<CustomAttributeData> attributes )
        {
            foreach( var a in attributes )
            {
                if( a.AttributeType == typeof( OptionNameAttribute ) )
                {
                    var s = ((string?)a.ConstructorArguments[0].Value)?.Trim();
                    if( !string.IsNullOrWhiteSpace( s ) )
                    {
                        var names = s.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
                        if( names.Length > 0
                            && names.All( n => !n.Contains( ' ' ) )
                            && names[0].StartsWith( "--" ) && names[0].Length > 2
                            && names.Skip( 1 ).All( n => n.Length > 1 && n[0] == '-' && n[1] != '-' ) )
                        {
                            return ImmutableCollectionsMarshal.AsImmutableArray( names );
                        }
                    }
                    Throw.CKException( $"""
                    Invalid [OptionName( "{s}" )] on '{p.ParameterType.Name} {p.Name}' in command method '{typeInfo.TypeName}.{method.Name}'.
                    There must be at least one option or flag name. The first name must start with "--" (long name),
                    the optional following ones, comma separated, must start with "-" (short names).
                    """ );
                }
            }
            return ["--" + System.Text.Json.JsonNamingPolicy.KebabCaseLower.ConvertName( p.Name! )];
        }

        static bool ExpectBooleanFlag( IPluginTypeInfo typeInfo, MethodInfo method, ParameterInfo p )
        {
            if( p.ParameterType != typeof( bool ) )
            {
                Throw.CKException( $"""
                    Invalid parameter type '{p.ParameterType.Name} {p.Name}' in command method '{typeInfo.TypeName}.{method.Name}'.
                    Only strings without default value (arguments), strings with a default value (options) and boolean (flags) are currently allowed
                    in this order (arguments must come first, then options and then flags).
                    """ );
            }
            if( p.HasDefaultValue && p.DefaultValue is true )
            {
                Throw.CKException( $"""
                    Invalid parameter 'bool {p.Name} = true' in command method '{typeInfo.TypeName}.{method.Name}'.
                    Flags can only default to false.
                    """ );
            }
            return true;
        }
    }

    static string NormalizePath( string commandPath )
    {
        commandPath = RegExPath().Replace( commandPath.Trim().ToLowerInvariant(), " " );
        return commandPath;
    }

    [GeneratedRegex( "\\s+", RegexOptions.CultureInvariant )]
    private static partial Regex RegExPath();
}
