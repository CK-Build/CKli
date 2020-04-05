using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CK.Env
{
    public class CommandRegister
    {
        readonly Dictionary<NormalizedPath, ICommandHandler> _commands;

        public CommandRegister()
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
                || h.UniqueName.Path.IndexOfAny( new char[] { '*', '?' } ) >= 0 ) throw new ArgumentException( "Command name must not be empty nor contain '*' or '?'.", nameof( ICommandHandler.UniqueName ) );
            _commands.Add( h.UniqueName, h );
        }

        /// <summary>
        /// Unregisters a command.
        /// </summary>
        /// <param name="uniqueName">The commande name.</param>
        public void Unregister( NormalizedPath uniqueName )
        {
            _commands.Remove( uniqueName );
        }

        class MethodHandler : ICommandHandler
        {
            readonly object _instance;
            readonly MethodInfo _method;
            readonly Func<bool> _enabled;
            readonly ParameterInfo[] _parameters;

            public MethodHandler( bool confirmationRequired, NormalizedPath n, object instance, MethodInfo method, ParallelCommandMode parallelMode, ParameterInfo[] parameters, Func<bool> enabled )
            {
                ConfirmationRequired = confirmationRequired;
                UniqueName = n;
                _instance = instance;
                _method = method;
                _parameters = parameters;
                PayloadSignature = parameters.Length == 1
                              ? null
                              : '(' + parameters.Skip( 1 ).Select( p => p.Name ).Concatenate() + ')';
                _enabled = enabled;
                ParallelMode = parallelMode;
            }

            public bool ConfirmationRequired { get; }

            public NormalizedPath UniqueName { get; }

            public bool GetEnabled() => _enabled != null ? _enabled() : true;

            public string PayloadSignature { get; }

            public ParallelCommandMode ParallelMode { get; }

            public object CreatePayload()
            {
                return PayloadSignature != null ? new SimplePayload( _parameters.Skip( 1 ) ) : null;
            }

            public void Execute( IActivityMonitor m, object payload )
            {
                using( m.OpenTrace( $"Executing {UniqueName}." ) )
                {
                    try
                    {
                        if( PayloadSignature != null )
                        {
                            if( !(payload is SimplePayload simple)
                                || simple.Fields.Count != _parameters.Length - 1 )
                            {
                                throw new ArgumentException( nameof( payload ) );
                            }
                            var p = new object[_parameters.Length];
                            p[0] = m;
                            for( int i = 0; i < simple.Fields.Count; ++i )
                            {
                                p[i + 1] = simple.Fields[i].GetValue();
                            }
                            _method.Invoke( _instance, p );
                        }
                        else
                        {
                            _method.Invoke( _instance, new[] { m } );
                        }
                    }
                    catch( Exception ex )
                    {
                        m.Error( ex );
                    }
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
        /// <param name="parallelMode">Parallel command mode.</param>
        /// <param name="enabled">A companion function that knows how to compute whether the command is enabled or not.</param>
        /// <returns>The registered command handler.</returns>
        public ICommandHandler Register( bool confirmationRequired, NormalizedPath uniqueName, object o, MethodInfo method, ParallelCommandMode parallelMode, Func<bool> enabled = null )
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
            var h = new MethodHandler( confirmationRequired, uniqueName, o, method, parallelMode, parameters, enabled );
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
                    Register( attr.ConfirmationRequired, provider.CommandProviderName.AppendPart( m.Name ), provider, m, attr.ParallelMode, enabled );
                }
            }
        }

        Func<bool> GetEnabledMethod( object instance, MethodInfo[] methods, string commandMethodName )
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
                return () => (bool)enabled.Invoke( instance, Array.Empty<object>() );
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
        public void UnregisterAll( Func<ICommandHandler, bool> keepFilter = null )
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
        /// Gets the registered commands that match a pattern with wildcards ('*' and '?').
        /// </summary>
        /// <param name="pattern">The pattern. Must not be null or empty.</param>
        /// <param name="checkEnabled">False to also return currently disabled commands.</param>
        /// <returns>The set of commands.</returns>
        public IEnumerable<ICommandHandler> GetCommands( string pattern, bool checkEnabled = true )
        {
            if( String.IsNullOrWhiteSpace( pattern ) ) throw new ArgumentNullException( nameof( pattern ) );
            // Escaping is not required (ther cannot be * or ? in name, this is tested in registration): the pattern is simple.
            // Nevertheless, we must check for stupid patterns (or so-to-speak smart people trying to DoS us...):
            string cleaned;
            while( (cleaned = pattern.Replace( "**", "*" ).Replace( "*?", "*" ).Replace( "?*", "*" )) != pattern ) pattern = cleaned;
            pattern = '^' + Regex.Escape( cleaned ).Replace( "\\*", ".*" ).Replace( "\\?", "." ) + '$';
            return _commands.Values.Where( c => Regex.IsMatch( c.UniqueName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )
                                                && (!checkEnabled || c.GetEnabled()) );
        }

        /// <summary>
        /// Gets the command handler. Its <see cref="ICommandHandler.GetEnabled()"/> may be false.
        /// </summary>
        /// <param name="path">Path of the command.</param>
        /// <returns>The command handler or null.</returns>
        public ICommandHandler this[NormalizedPath path] => _commands.GetValueWithDefault( path, null );
    }
}
