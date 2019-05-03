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
        /// Returns the first tag object of the specified type from the list of tags
        /// of this <see cref="TaggedObject"/>.
        /// </summary>
        /// <param name="type">The type of the tag to retrieve.</param>
        /// <returns>
        /// The first matching tag object, or null if no tag is the specified type.
        /// </returns>
        object Tag( Type type );

        /// <summary>
        /// Returns the first tag object of the specified type from the list of tags
        /// of this <see cref="TaggedObject"/>.
        /// </summary>
        /// <typeparam name="T">The type of the tag to retrieve.</typeparam>
        /// <returns>
        /// The first matching tag object, or null if no tag
        /// is the specified type.
        /// </returns>
        T Tag<T>() where T : class;

        /// <summary>
        /// Returns an enumerable collection of tags of the specified type
        /// for this <see cref="TaggedObject"/>.
        /// </summary>
        /// <param name="type">The type of the tags to retrieve.</param>
        /// <returns>An enumerable collection of tags for this TaggedObject.</returns>
        IEnumerable<object> Tags( Type type );

        /// <summary>
        /// Returns an enumerable collection of tags of the specified type
        /// for this <see cref="TaggedObject"/>.
        /// </summary>
        /// <typeparam name="T">The type of the tags to retrieve.</typeparam>
        /// <returns>An enumerable collection of tags for this TaggedObject.</returns>
        IEnumerable<T> Tags<T>() where T : class;
    }
}
