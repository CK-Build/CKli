using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Core
{
    public static class ServiceProviderExtension
    {
        /// <summary>
        /// Attempts to locate a <see cref="ISimpleObjectActivator"/> service or falls back to
        /// a default <see cref="SimpleObjectActivator"/> and calls <see cref="ISimpleObjectActivator.Create"/>
        /// on it.
        /// Returns null on error.
        /// </summary>
        /// <param name="this">This <see cref="IServiceProvider"/>.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="t">Type to instanciate.</param>
        /// <param name="requiredParameters">Optional required parameters.</param>
        /// <returns>A new instance on success, null on error.</returns>
        public static object SimpleObjectCreate( this IServiceProvider @this, IActivityMonitor monitor, Type t, IEnumerable<object> requiredParameters )
        {
            if( monitor == null ) throw new ArgumentNullException( nameof( monitor ) );
            ISimpleObjectActivator activator = @this.GetService<ISimpleObjectActivator>();
            if( activator == null )
            {
                monitor.Info( "No registered ISimpleObjectActivator found. Using transient new SimpleObjectActivator()." );
                activator = new SimpleObjectActivator();
            }
            return activator.Create( monitor, t, @this, requiredParameters );
        }

        /// <summary>
        /// Attempts to locate a <see cref="ISimpleObjectActivator"/> service or falls back to
        /// a default <see cref="SimpleObjectActivator"/> and calls <see cref="ISimpleObjectActivator.Create"/>
        /// on it.
        /// Returns null on error.
        /// </summary>
        /// <param name="this">This <see cref="IServiceProvider"/>.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="t">Type to instanciate.</param>
        /// <param name="requiredParameter">Required parameter. Must not be null.</param>
        /// <returns>A new instance on success, null on error.</returns>
        public static object SimpleObjectCreate( this IServiceProvider @this, IActivityMonitor monitor, Type t, object requiredParameter )
        {
            if( requiredParameter == null ) throw new ArgumentNullException( nameof( requiredParameter ) );
            return SimpleObjectCreate( @this, monitor, t, new[] { requiredParameter } );
        }

        /// <summary>
        /// Attempts to locate a <see cref="ISimpleObjectActivator"/> service or falls back to
        /// a default <see cref="SimpleObjectActivator"/> and calls <see cref="ISimpleObjectActivator.Create"/>
        /// on it.
        /// Returns null on error.
        /// </summary>
        /// <param name="this">This <see cref="IServiceProvider"/>.</param>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="t">Type to instanciate.</param>
        /// <returns>A new instance on success, null on error.</returns>
        public static object SimpleObjectCreate( this IServiceProvider @this, IActivityMonitor monitor, Type t )
        {
            return SimpleObjectCreate( @this, monitor, t, Array.Empty<object>() );
        }
    }
}
