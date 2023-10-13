using CK.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

#nullable enable

namespace CK.Env
{
    public class CommandRegistry
    {
        readonly Dictionary<NormalizedPath, ICommandHandler> _commands;

        public CommandRegistry()
        {
            _commands = new Dictionary<NormalizedPath, ICommandHandler>();
        }

        /// <summary>
        /// Registers a command. Its <see cref="ICommandHandler.UniqueName"/> must not
        /// already be registered otherwise an exception is thrown.
        /// </summary>
        /// <param name="h">The command handler.</param>
        public void Register( ICommandHandler h )
        {
            if( h.UniqueName.IsEmptyPath
                || h.UniqueName.Path.IndexOfAny( new char[] { '*', '?' } ) >= 0 )
            {
                Throw.ArgumentException( "Command name must not be empty nor contain '*' or '?'.", nameof( ICommandHandler.UniqueName ) );
            }
            _commands.Add( h.UniqueName, h );
        }

        /// <summary>
        /// Unregisters a command.
        /// </summary>
        /// <param name="uniqueName">The command name.</param>
        public void Unregister( NormalizedPath uniqueName )
        {
            _commands.Remove( uniqueName );
        }

        class MethodHandler : ICommandHandler
        {
            readonly object _instance;
            readonly MethodInfo _method;
            readonly Func<bool>? _enabled;
            readonly ParameterInfo[] _parameters;

            public MethodHandler( bool confirmationRequired, NormalizedPath n, object instance, MethodInfo method, ParameterInfo[] parameters, Func<bool>? enabled )
            {
                if( !IsValidCommandName( n ) ) throw new ArgumentException( $"Invalid characters in command name: {n}." );
                ConfirmationRequired = confirmationRequired;
                UniqueName = n;
                _instance = instance;
                _method = method;
                _parameters = parameters;
                PayloadSignature = parameters.Length == 1
                              ? null
                              : '(' + parameters.Skip( 1 ).Select( p => p.Name ).Concatenate() + ')';
                _enabled = enabled;
            }

            static readonly char[] _invalidCommandNameChar = Path.GetInvalidPathChars().Concat( new char[] { '*', '?', '|', '<', '>' } ).Distinct().ToArray();
 
            static public bool IsValidCommandName( NormalizedPath name )
            {
                return name.Path.IndexOfAny( _invalidCommandNameChar ) < 0;
            }

            public bool ConfirmationRequired { get; }

            public NormalizedPath UniqueName { get; }

            public bool GetEnabled() => _enabled == null || _enabled();

            public string? PayloadSignature { get; }

            public object? CreatePayload()
            {
                return PayloadSignature != null ? new SimplePayload( _parameters.Skip( 1 ) ) : null;
            }

            public void UnsafeExecute( IActivityMonitor m, object? payload )
            {
                object[] p;
                if( PayloadSignature != null )
                {
                    if( payload is not SimplePayload simple
                        || simple.Fields.Count != _parameters.Length - 1 )
                    {
                        throw new ArgumentException( nameof( payload ) );
                    }
                    p = new object[_parameters.Length];
                    p[0] = m;
                    for( int i = 0; i < simple.Fields.Count; ++i )
                    {
                        p[i + 1] = simple.Fields[i].GetValue();
                    }
                }
                else p = new[] { m };
                try
                {
                    _method.Invoke( _instance, p );
                }
                catch( TargetInvocationException ex )
                {
                    ExceptionDispatchInfo.Capture( ex.InnerException ?? ex ).Throw();
                }
            }
        }

        /// <summary>
        /// Registers a method that can be called as a command.
        /// </summary>
        /// <param name="confirmationRequired">True to confirm the command before its execution.</param>
        /// <param name="uniqueName">The unique command name.</param>
        /// <param name="o">The object instance that holds the method.</param>
        /// <param name="method">The method.</param>
        /// <param name="enabled">A companion function that knows how to compute whether the command is enabled or not.</param>
        /// <returns>The registered command handler.</returns>
        public ICommandHandler Register( bool confirmationRequired, NormalizedPath uniqueName, object o, MethodInfo method, Func<bool>? enabled = null )
        {
            if( _commands.ContainsKey( uniqueName ) )
            {
                throw new ArgumentException( $"Already registered command '{uniqueName}'.", nameof( uniqueName ) );
            }
            var parameters = method.GetParameters();
            if( parameters.Length == 0 || !typeof( IActivityMonitor ).IsAssignableFrom( parameters[0].ParameterType ) )
            {
                throw new ArgumentException( $"Method {method} for command '{uniqueName}' must have a first IActivityMonitor parameter.", nameof( method ) );
            }
            var h = new MethodHandler( confirmationRequired, uniqueName, o, method, parameters, enabled );
            _commands.Add( uniqueName, h );
            return h;
        }

        /// <summary>
        /// Registers a <see cref="ICommandMethodsProvider"/> by analyzing its public methods.
        /// </summary>
        /// <param name="provider">The command provider.</param>
        public void Register( ICommandMethodsProvider provider )
        {
            var methods = provider?.GetType().GetMethods() ?? throw new ArgumentNullException( nameof( provider ) );
            foreach( var m in methods )
            {
                var attr = m.GetCustomAttribute<CommandMethodAttribute>();
                if( attr != null )
                {
                    var enabled = GetEnabledMethod( provider, methods, m.Name );
                    Register( attr.ConfirmationRequired, provider.CommandProviderName.AppendPart( m.Name ), provider, m, enabled );
                }
            }
        }

        Func<bool>? GetEnabledMethod( object instance, MethodInfo[] methods, string commandMethodName )
        {
            var isEnabledName = $"Is{commandMethodName}Enabled";
            var canName = $"Can{commandMethodName}";
            var enabledNames = new string[] { isEnabledName, "get_" + isEnabledName, canName, "get_" + canName };
            var enabled = methods.FirstOrDefault( e => enabledNames.Contains( e.Name ) );
            if( enabled != null )
            {
                if( enabled.GetParameters().Length > 0 || enabled.ReturnType != typeof( bool ) )
                {
                    throw new Exception( $"Method {enabled} must not have any parameter and return a boolean." );
                }
                return () => (bool)enabled.Invoke( instance, Array.Empty<object>() )!;
            }
            return null;
        }

        /// <summary>
        /// Unregisters all commands from a <see cref="ICommandMethodsProvider"/>.
        /// </summary>
        /// <param name="provider">The command provider.</param>
        public void Unregister( ICommandMethodsProvider provider )
        {
            var r = GetCommands( provider.CommandProviderName.AppendPart( "*" ) ).ToList();
            foreach( var p in r )
            {
                _commands.Remove( p.UniqueName );
            }
        }

        /// <summary>
        /// Unregisters all commands, optionally keeping some of them.
        /// </summary>
        /// <param name="keepFilter">Optional function that can return true to keep the handler registered.</param>
        public void UnregisterAll( Func<ICommandHandler, bool>? keepFilter = null )
        {
            if( keepFilter == null ) _commands.Clear();
            else
            {
                var cmd = _commands.Values.ToArray();
                foreach( var c in cmd )
                {
                    if( !keepFilter( c ) ) _commands.Remove( c.UniqueName );
                }
            }
        }

        /// <summary>
        /// Gets all the registered commands.
        /// </summary>
        /// <param name="checkEnabled"></param>
        /// <param name="checkEnabled">False to also return currently disabled commands.</param>
        /// <returns>The set of commands.</returns>
        public IEnumerable<ICommandHandler> GetAllCommands( bool checkEnabled = true )
        {
            return checkEnabled ? _commands.Values.Where( c => c.GetEnabled() ) : _commands.Values;
        }

        /// <summary>
        /// Gets the registered commands that match a pattern with wildcards ('*' and '?') or multiple patterns separated by '|'.
        /// </summary>
        /// <param name="pattern">The pattern. Must not be null or empty.</param>
        /// <param name="checkEnabled">False to also return currently disabled commands.</param>
        /// <returns>The set of commands.</returns>
        public IEnumerable<ICommandHandler> GetCommands( string pattern, bool checkEnabled = true )
        {
            if( String.IsNullOrWhiteSpace( pattern ) ) throw new ArgumentNullException( nameof( pattern ) );

            // Escaping is not required (there cannot be * or ? in name, this is tested in registration): the pattern is simple.
            // Nevertheless, we must check for stupid patterns (or so-to-speak smart people trying to DoS us...):
            if( pattern.IndexOf( '|' ) >= 0 )
            {
                pattern = String.Join( "|", pattern.Split( new[] { '|' }, StringSplitOptions.RemoveEmptyEntries  ).Select( p => NormalizePattern( p ) ) );
            }
            else pattern = NormalizePattern( pattern );

            return _commands.Values.Where( c => Regex.IsMatch( c.UniqueName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )
                                                && (!checkEnabled || c.GetEnabled()) );

            static string NormalizePattern( string p )
            {
                string cleaned;
                while( (cleaned = p.Replace( "**", "*" ).Replace( "*?", "*" ).Replace( "?*", "*" )) != p ) p = cleaned;
                return '^' + Regex.Escape( cleaned ).Replace( "\\*", ".*" ).Replace( "\\?", "." ) + '$';
            }
        }

        /// <summary>
        /// Gets the command handler. Its <see cref="ICommandHandler.GetEnabled()"/> may be false.
        /// </summary>
        /// <param name="path">Path of the command.</param>
        /// <returns>The command handler or null.</returns>
        public ICommandHandler? this[NormalizedPath path] => _commands.GetValueOrDefault( path );
    }
}
