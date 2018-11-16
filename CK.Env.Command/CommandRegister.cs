using CK.Core;
using CK.Text;
using DotNet.Globbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            readonly Type _payloadType;
            readonly Func<bool> _enabled;

            public MethodHandler( NormalizedPath n, object instance, MethodInfo method, Type payloadType, Func<bool> enabled )
            {
                _instance = instance;
                _method = method;
                _payloadType = payloadType;
                _enabled = enabled;
            }

            public NormalizedPath UniqueName { get; }

            public bool GetEnabled() => _enabled != null ? _enabled() : true;

            public bool HasPayload => _payloadType != null;

            public object CreatePayload()
            {
                return _payloadType != null ? Activator.CreateInstance( _payloadType ) : null;
            }

            public void Handle( IActivityMonitor m, object payload )
            {
                if( _payloadType != null )
                {
                    _method.Invoke( _instance, new[] { m, payload } );
                }
                else
                {
                    _method.Invoke( _instance, new[] { m } );
                }
            }
        }

        public ICommandHandler Register( NormalizedPath uniqueName, object o, MethodInfo method, Func<bool> enabled = null )
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
            if( parameters.Length > 2 )
            {
                throw new ArgumentException( $"Method {method} for command '{uniqueName}' must have at most 2 parameters.", nameof( method ) );
            }
            var h = new MethodHandler( uniqueName, o, method, parameters.Length == 2 ? parameters[1].ParameterType : null, enabled );
            _commands.Add( uniqueName, h );
            return h;
        }

        public ICommandHandler Register( NormalizedPath uniqueName, object o, string commandMethodName )
        {
            var methods = o.GetType().GetMethods();
            var m = methods.Single( method => method.Name == commandMethodName );
            var enabled = GetEnabledMethod( o, methods, m.Name );
            return Register( uniqueName, o, m, enabled );
        }

        public void Register( ICommandMethodsProvider provider )
        {
            var methods = provider.GetType().GetMethods();
            foreach( var m in methods.Where( m => m.GetCustomAttribute<CommandMethodAttribute>() != null ) )
            {
                var enabled = GetEnabledMethod( provider, methods, m.Name );
                Register( provider.CommandProviderName.AppendPart( m.Name ), provider, m, enabled );
            }
        }

        Func<bool> GetEnabledMethod( object instance, MethodInfo[] methods, string commandMethodName )
        {
            var enabled = methods.FirstOrDefault( e => e.Name == $"Is{commandMethodName}Enabled" );
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
            var r = Select( provider.CommandProviderName.AppendPart( "*" ) );
            foreach( var p in r )
            {
                _commands.Remove( p.UniqueName );
            }
        }

        public List<ICommandHandler> Select( NormalizedPath globPattern, bool checkEnabled = true )
        {
            var result = new List<ICommandHandler>();
            var p = Glob.Parse( globPattern );
            foreach( var c in _commands.Values )
            {
                if( p.IsMatch( c.UniqueName ) && (!checkEnabled || c.GetEnabled() ) ) result.Add( c );
            }
            return result;
        }

    }
}
