using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Simple base class to support typed Tags for a simple object extensibility.
    /// </summary>
    public class TaggedObject : ITaggedObject
    {
        object _tag;

        /// <summary>
        /// Adds an object to the tag list of this <see cref="TaggedObject"/>.
        /// </summary>
        /// <param name="tag">The tag to add.</param>
        public void AddTag( object tag )
        {
            if( tag == null ) throw new ArgumentNullException( "tag" );
            if( _tag == null )
            {
                _tag = tag is object[] ? new object[] { tag } : tag;
            }
            else
            {
                object[] a = _tag as object[];
                if( a == null )
                {
                    _tag = new object[] { _tag, tag };
                }
                else
                {
                    int i = 0;
                    while( i < a.Length && a[i] != null ) i++;
                    if( i == a.Length )
                    {
                        Array.Resize( ref a, i * 2 );
                        _tag = a;
                    }
                    a[i] = tag;
                }
            }
        }

        /// <summary>
        /// Returns the first tag object of the specified type from the list of tags
        /// of this <see cref="TaggedObject"/>.
        /// </summary>
        /// <param name="type">The type of the tag to retrieve.</param>
        /// <returns>
        /// The first matching tag object, or null if no tag is the specified type.
        /// </returns>
        public object Tag( Type type )
        {
            if( type == null ) throw new ArgumentNullException( "type" );
            if( _tag != null )
            {
                object[] a = _tag as object[];
                if( a == null )
                {
                    if( type.IsInstanceOfType( _tag ) ) return _tag;
                }
                else
                {
                    for( int i = 0; i < a.Length; i++ )
                    {
                        object obj = a[i];
                        if( obj == null ) break;
                        if( type.IsInstanceOfType( obj ) ) return obj;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the first tag object of the specified type from the list of tags
        /// of this <see cref="TaggedObject"/>.
        /// </summary>
        /// <typeparam name="T">The type of the tag to retrieve.</typeparam>
        /// <returns>
        /// The first matching tag object, or null if no tag
        /// is the specified type.
        /// </returns>
        public T Tag<T>() where T : class => (T)Tag( typeof( T ) );

        /// <summary>
        /// Returns an enumerable collection of tags of the specified type
        /// for this <see cref="TaggedObject"/>.
        /// </summary>
        /// <param name="type">The type of the tags to retrieve.</param>
        /// <returns>An enumerable collection of tags for this TaggedObject.</returns>
        public IEnumerable<object> Tags( Type type )
        {
            if( type == null ) throw new ArgumentNullException( "type" );
            if( _tag != null )
            {
                object[] a = _tag as object[];
                if( a == null )
                {
                    if( type.IsInstanceOfType( _tag ) ) yield return _tag;
                }
                else
                {
                    for( int i = 0; i < a.Length; i++ )
                    {
                        object obj = a[i];
                        if( obj == null ) break;
                        if( type.IsInstanceOfType( obj ) ) yield return obj;
                    }
                }
            }
        }

        /// <summary>
        /// Returns an enumerable collection of tags of the specified type
        /// for this <see cref="TaggedObject"/>.
        /// </summary>
        /// <typeparam name="T">The type of the tags to retrieve.</typeparam>
        /// <returns>An enumerable collection of tags for this TaggedObject.</returns>
        public IEnumerable<T> Tags<T>() where T : class => Tags( typeof( T ) ).Cast<T>();

        /// <summary>
        /// Removes the tags of the specified type from this <see cref="TaggedObject"/>.
        /// </summary>
        /// <param name="type">The type of tags to remove.</param>
        public void RemoveTags( Type type )
        {
            if( type == null ) throw new ArgumentNullException( "type" );
            if( _tag != null )
            {
                object[] a = _tag as object[];
                if( a == null )
                {
                    if( type.IsInstanceOfType( _tag ) ) _tag = null;
                }
                else
                {
                    int i = 0, j = 0;
                    while( i < a.Length )
                    {
                        object obj = a[i];
                        if( obj == null ) break;
                        if( !type.IsInstanceOfType( obj ) ) a[j++] = obj;
                        i++;
                    }
                    if( j == 0 )
                    {
                        _tag = null;
                    }
                    else
                    {
                        while( j < i ) a[j++] = null;
                    }
                }
            }
        }

        /// <summary>
        /// Removes the tags of the specified type from this <see cref="TaggedObject"/>.
        /// </summary>
        /// <typeparam name="T">The type of tags to remove.</typeparam>
        public void RemoveTags<T>() where T : class => RemoveTags( typeof( T ) );

    }
}
