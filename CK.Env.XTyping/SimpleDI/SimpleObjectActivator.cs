using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CK.Core
{
    using Required = IReadOnlyList<KeyValuePair<object, Type>>;

    public class SimpleObjectActivator : ISimpleObjectActivator
    {
        /// <summary>
        /// Creates an instance of the specified type, using any available services.
        /// </summary>
        /// <param name="monitor">Monitor to use.</param>
        /// <param name="t">Type of the object to create.</param>
        /// <param name="services">Available services to inject.</param>
        /// <param name="requiredParameters">Optional required parameters.</param>
        /// <returns>The object instance or null on error.</returns>
        public object Create( IActivityMonitor monitor, Type t, IServiceProvider services, IEnumerable<object> requiredParameters = null )
        {
            if( monitor == null ) throw new ArgumentNullException( nameof( monitor ) );
            if( t == null ) throw new ArgumentNullException( nameof( t ) );
            using( monitor.OpenDebug( $"Creating instance of type: {t.AssemblyQualifiedName}." ) )
            try
            {
                Required required = requiredParameters == null
                        ? Array.Empty<KeyValuePair<object, Type>>()
                        : (Required)requiredParameters.Select( r => new KeyValuePair<object, Type>( r, r.GetType() ) ).ToList();

                var longestCtor = t.GetTypeInfo().GetConstructors()
                                    .Select( x => ValueTuple.Create( x, x.GetParameters() ) )
                                    .Where( x => x.Item2.Length >= required.Count )
                                    .OrderByDescending( x => x.Item2.Length )
                                    .Select( x => new
                                    {
                                        Ctor = x.Item1,
                                        Parameters = x.Item2,
                                        Mapped = x.Item2
                                                    .Select( p => required.FirstOrDefault( r => p.ParameterType.IsAssignableFrom( r.Value ) ).Key )
                                                    .ToArray()
                                    } )
                                    .Where( x => x.Mapped.Count( m => m != null ) == required.Count )
                                    .FirstOrDefault();
                if( longestCtor == null )
                {
                    var msg = $"Unable to find a public constructor for '{t.FullName}'.";
                    if( required.Count > 0 )
                    {
                        msg += " With required parameters compatible with type: " + required.Select( r => r.Value.Name ).Concatenate();
                    }
                    monitor.Error( msg );
                    return null;
                }
                int failCount = 0;
                for( int i = 0; i < longestCtor.Mapped.Length; ++i )
                {
                    if( longestCtor.Mapped[i] == null )
                    {
                        var p = longestCtor.Parameters[i];
                        var resolved = services.GetService( p.ParameterType );
                        if( resolved == null && !p.HasDefaultValue )
                        {
                            monitor.Error( $"Resolution failed for parameter '{p.Name}', type: '{p.ParameterType.Name}'." );
                            ++failCount;
                        }
                        longestCtor.Mapped[i] = resolved;
                    }
                }
                if( failCount > 0 )
                {
                    monitor.OpenError( $"Unable to resolve parameters for '{t.FullName}'. Considered longest constructor: {longestCtor.Ctor.ToString()}." );
                    return null;
                }
                return longestCtor.Ctor.Invoke( longestCtor.Mapped );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While instanciating {t.FullName}.", ex );
                return null;
            }
        }
    }
}
