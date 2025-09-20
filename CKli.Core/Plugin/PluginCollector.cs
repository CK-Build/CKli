using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core;

public sealed class PluginCollector
{
    readonly HashSet<Type> _cycleDetector;
    readonly Dictionary<Type, Factory> _factories;

    public PluginCollector()
    {
        _cycleDetector = new HashSet<Type>();
        _factories = new Dictionary<Type, Factory>();
    }

    public void AddPrimaryPlugin<T>() where T : WorldPlugin
    {
        AddPlugin( typeof( T ), true );
        _cycleDetector.Clear();
    }

    public void AddSupportPlugin<T>() where T : WorldPlugin
    {
        AddPlugin( typeof( T ), false );
        _cycleDetector.Clear();
    }

    sealed class Factory
    {
        readonly ConstructorInfo _ctor;
        readonly Factory?[] _deps;
        readonly ParameterInfo[] _params;
        readonly int _worldParameterIndex;
        internal bool _primary;

        public Factory( ConstructorInfo ctor, Factory?[] deps, ParameterInfo[] parameters, int worldParameterIndex, bool primary )
        {
            _ctor = ctor;
            _deps = deps;
            _params = parameters;
            _worldParameterIndex = worldParameterIndex;
            _primary = primary;
        }
    }

    Factory? AddPlugin( Type type, bool primary )
    {
        if( _factories.TryGetValue( type, out var factory ) )
        {
            if( !factory._primary && primary ) factory._primary = true;
        }
        if( !type.IsClass || !type.IsSealed )
        {
            Throw.CKException( $"Registered WorldPlugin '{type.Name}' must be a sealed class." );
        }
        var ctors = type.GetConstructors();
        if( ctors.Length != 1 )
        {
            Throw.CKException( $"WorldPlugin '{type.Name}' must have a single public constructor (found {ctors.Length})." );
        }
        var ctor = ctors[0];
        var parameters = ctor.GetParameters();
        var deps = new Factory?[parameters.Length];
        int worldParameterIndex = -1;
        _cycleDetector.Add( type );
        foreach( var p in parameters )
        {
            Type parameterType = p.ParameterType;
            if( parameterType ==  typeof( World ) )
            {
                if( worldParameterIndex != -1 )
                {
                    Throw.CKException( $"Duplicate World parameter in WorldPlugin '{type.Name}' constructor ('{parameters[worldParameterIndex].Name}' and '{p.Name}')." );
                }
                worldParameterIndex = p.Position;
            }
            else if( !typeof(WorldPlugin).IsAssignableFrom( parameterType ) )
            {
                Throw.CKException( $"Invalid parameter in WorldPlugin '{type.Name}' constructor: {p.Name} must be a WorldPlugin (not a '{parameterType.Name}')." );
            }
            else
            {
                if( _cycleDetector.Contains( parameterType ) )
                {
                    Throw.CKException( $"Dependenct cycle detected between WorldPlugin '{type.Name}( {parameterType.Name} {p.Name} )': '{parameterType.Name}' also depends on '{type.Name}'." );
                }
                var f = AddPlugin( parameterType, false );
                if( f == null )
                {
                    // Disabled plugin: if the parameter is not optional, we are transitively disabled.
                    // It is useless to continue.
                    if( !p.HasDefaultValue )
                    {
                        return null;
                    }
                }
                else
                {
                    deps[p.Position] = f;
                }
            }
        }
        return new Factory( ctor, deps, parameters, worldParameterIndex, primary );
    }

    internal IServiceProvider BuildServices( World world ) => new Plugins( world, _factories );

    internal sealed class Plugins : IServiceProvider, IDisposable
    {
        readonly Dictionary<Type, object> _instances;
        readonly Dictionary<Type, Factory> _factories;

        Plugins( World world, Dictionary<Type, object> instances )
        {
            _services = new Dictionary<Type, object?>( types.Count + 2 )
            {
                { typeof( IServiceProviderIsService ), this },
                { typeof( World ), w }
            };
            foreach( var type in types )
            {
                GetService( type );
            }

            _factories = factories;
        }

        public void Dispose()
        {
            foreach( var service in _services.Values )
            {
                if( service is IDisposable d ) d.Dispose();
            }
        }

        public object? GetService( Type serviceType )
        {
            if( _services.TryGetValue( serviceType, out var service ) )
            {
                if( service == null )
                {
                    Throw.CKException( $"Cyclic dependency detected for service '{serviceType.FullName}'." );
                }
                return service;
            }
            _services.Add( serviceType, null );
            service = ActivatorUtilities.CreateInstance( this, serviceType );
            _services[serviceType] = service;
            return service;
        }
    }

}

