using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Extends <see cref="XTypedObject"/>.
    /// </summary>
    public static class XTypedObjectExtensions
    {
        /// <summary>
        /// Enumerates the <see cref="TopDescendants{T}(IEnumerable{XTypedObject})"/> of a set of <see cref="XTypedObject"/>.
        /// </summary>
        /// <typeparam name="T">The type to select.</typeparam>
        /// <param name="roots">The starting objects to consider.</param>
        /// <returns>
        /// The top <typeparamref name="T"/> elements below the <paramref name="roots"/> (or a root itself if it is a <typeparamref name="T"/>).
        /// </returns>
        public static IEnumerable<T> TopDescendants<T>( this IEnumerable<XTypedObject> roots ) where T : XTypedObject
        {
            return roots.SelectMany( r => r.TopDescendants<T>( withSelf: true ) );
        }

    }
}
