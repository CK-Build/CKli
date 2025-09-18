using CK.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Core;

public sealed class WorldServiceCollection
{
    readonly HashSet<Type> _serviceTypes;

    public WorldServiceCollection()
    {
        _serviceTypes = new HashSet<Type>();
    }

    public void Add<T>() where T : WorldService
    {
        var type = typeof(T);
        if( _serviceTypes.Add( type ) )
        {
            if( !type.IsClass || !type.IsSealed )
            {
                Throw.CKException( $"Registered WorlService '{type.Name}' must be a sealed class." );
            }
        }
    }

    internal IServiceProvider BuildServices( World world ) => new WorldServices( world, _serviceTypes );

    sealed class WorldServices : IServiceProvider, IServiceProviderIsService, IDisposable
    {
        readonly Dictionary<Type, object?> _services;

        public WorldServices( World w, HashSet<Type> types )
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

        /// <summary>
        /// Whether we return true or false here doesn't change anything to the ActivatorUtilities.CreateInstance
        /// behavior.
        /// </summary>
        public bool IsService( Type serviceType ) => false;
    }

}

