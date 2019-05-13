using System;
using System.Collections.Generic;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Simple object extensibility support for Typed Tags.
    /// </summary>
    public interface ITaggedObject
    {
        /// <summary>
        /// Gets or sets a non null tag object of the specified type.
        /// </summary>
        /// <param name="type">The type of the tag to retrieve or sets.</param>
        /// <param name="newValue">Non null value of type <paramref name="type"/> to be set as the new tag value.</param>
        /// <returns>
        /// When <paramref name="newValue"/> is null, fhe first tag of the type, or null if no tag is the specified type.
        /// Otherwise, newValue is always returned.
        /// </returns>
        object Tag( Type type, object newValue = null );

        /// <summary>
        /// Gets or sets a non null tag object of the specified type..
        /// </summary>
        /// <typeparam name="T">The type of the tag to retrieve or set.</typeparam>
        /// <param name="newValue">Non null value to be set as the new tag value.</param>
        /// <returns>
        /// When <paramref name="newValue"/> is null, fhe first tag of the type, or null if no tag is the specified type.
        /// Otherwise, newValue is always returned.
        /// </returns>
        T Tag<T>( T newValue = null ) where T : class;

        /// <summary>
        /// Removes the tags of the specified type.
        /// </summary>
        /// <param name="type">The type of tags to remove.</param>
        void RemoveTags( Type type );

        /// <summary>
        /// Removes the tags of the specified type.
        /// </summary>
        /// <typeparam name="T">The type of tags to remove.</typeparam>
        void RemoveTags<T>() where T : class;
    }
}
