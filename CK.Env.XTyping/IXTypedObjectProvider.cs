using CK.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// This interface standardizes the fact that a <see cref="XTypedObject"/> usually define
    /// an actual object instance of a given type.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    public interface IXTypedObjectProvider<T>
    {
        /// <summary>
        /// Must return the instance. Null is a valid value at this level.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The instance (may be null).</returns>
        T GetObject( IActivityMonitor m );
    }
}
