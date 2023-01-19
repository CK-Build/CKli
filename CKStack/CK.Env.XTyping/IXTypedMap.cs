using System;
using System.Reflection;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Provides mapping from <see cref="XName"/> to <see cref="Type"/> that are <see cref="XTypedObject"/>.
    /// </summary>
    public interface IXTypedMap
    {
        /// <summary>
        /// Gets the type associated to a xml element or attribute name.
        /// </summary>
        /// <param name="n">The xml name.</param>
        /// <returns>The associated type if any.</returns>
        Type? GetNameMappping( XName n );

        /// <summary>
        /// Gets whether this map handles all mappings that may be
        /// found in a given assembly.
        /// </summary>
        /// <param name="a">The assembly.</param>
        /// <returns>True if the assembly is already handled by this map.</returns>
        bool HasAlreadyRegistered( Assembly a );
    }
}
