using System;
using System.Collections.Generic;

namespace CK.Core
{
    /// <summary>
    /// Simplest object instanciator.
    /// </summary>
    public interface ISimpleObjectActivator
    {
        /// <summary>
        /// Creates an instance of the specified type, using any available services.
        /// </summary>
        /// <param name="monitor">Monitor to use.</param>
        /// <param name="t">Type of the object to create.</param>
        /// <param name="services">Available services to inject.</param>
        /// <param name="requiredParameters">Optional required parameters.</param>
        /// <returns>The object instance or null on error.</returns>
        object Create( IActivityMonitor monitor, Type t, IServiceProvider services, IEnumerable<object> requiredParameters = null );
    }
}
