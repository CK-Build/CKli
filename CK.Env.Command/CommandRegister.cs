using CK.Core;
using CK.Text;
using DotNet.Globbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CK.Env
{
    public class CommandRegister
    {
        readonly Dictionary<NormalizedPath, ICommandHandler> _commands;

        public CommandRegister()
        {
            _commands = new Dictionary<NormalizedPath, ICommandHandler>();
        }

        public void Register( ICommandHandler h )
        {
            _commands.Add( h.UniqueName, h );
        }

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

            public MethodHandler( bool confirmationRequired,bool? parallelRun, bool? backgroundRun, NormalizedPath n, object instance, MethodInfo method, ParameterInfo[] parameters, Func<bool> enabled )
            {
                ConfirmationRequired = confirmationRequired;
                UniqueName = n;
                _instance = instance;
                _method = method;
                _parameters = parameters;
                PayloadSignature = parameters.Length == 1
                              ? null
                              : '(' + parameters.Skip(1).Select( p => p.Name ).Concatenate() + ')';
                _enabled = enabled;
                ParallelRun = parallelRun;
                BackgroundRun = backgroundRun;
            }

            public bool ConfirmationRequired { get; }

            public NormalizedPath UniqueName { get; }

            public bool GetEnabled() => _enabled != null ? _enabled() : true;

            public string PayloadSignature { get; }

            public bool? ParallelRun { get; }

            public bool? BackgroundRun { get; }

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

        public ICommandHandler Register( bool confirmationRequired, NormalizedPath uniqueName, object o, MethodInfo method, Func<bool> enabled = null )
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
            var parallelAttribute = method.GetCustomAttribute<ParallelCommandAttribute>();
            var backgroundAttribute = method.GetCustomAttribute<BackgroundCommandAttribute>();
            bool? runParallel = parallelAttribute == null ? false : parallelAttribute.AlwaysRunInParallel ? true : (bool?)null;
            bool? runBackground = backgroundAttribute == null ? false : backgroundAttribute.AlwaysRunInBackground ? true : (bool?)null;
            var h = new MethodHandler( confirmationRequired, runParallel, runBackground, uniqueName, o, method, parameters, enabled );
            _commands.Add( uniqueName, h );
            return h;
        }

        public ICommandHandler Register( bool confirmationRequired, NormalizedPath uniqueName, object o, string commandMethodName )
        {
            var methods = o.GetType().GetMethods();
            var m = methods.Single( method => method.Name == commandMethodName );
            var enabled = GetEnabledMethod( o, methods, m.Name );
            return Register( confirmationRequired, uniqueName, o, m, enabled );
        }

        public void Register( ICommandMethodsProvider provider )
        {
            var methods = provider.GetType().GetMethods();
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

        public void Unregister( ICommandMethodsProvider provider )
        {
            var r = GetCommands( provider.CommandProviderName.AppendPart( "*" ) ).ToList();
            foreach( var p in r )
            {
                _commands.Remove( p.UniqueName );
            }
        }

        public void UnregisterAll()
        {
            _commands.Clear();
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
        /// Gets the registered commands that match a pattern.
        /// </summary>
        /// <param name="globPattern">The pattern. Must not be null or empty.</param>
        /// <param name="checkEnabled">False to also return currently disabled commands.</param>
        /// <returns>The set of commands.</returns>
        public IEnumerable<ICommandHandler> GetCommands( string globPattern, bool checkEnabled = true )
        {
            if( String.IsNullOrWhiteSpace( globPattern ) ) throw new ArgumentNullException( nameof( globPattern ) );
            var p = Glob.Parse( globPattern );
            return _commands.Values.Where( c => p.IsMatch( c.UniqueName ) && (!checkEnabled || c.GetEnabled()) );
        }

        /// <summary>
        /// Gets the command handler. Its <see cref="ICommandHandler.GetEnabled()"/> may be false.
        /// </summary>
        /// <param name="path">Path of the command.</param>
        /// <returns>The command handler or null.</returns>
        public ICommandHandler this[NormalizedPath path] => _commands.GetValueWithDefault( path, null );
    }
}
