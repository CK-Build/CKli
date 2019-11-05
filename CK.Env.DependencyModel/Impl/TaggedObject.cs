using System;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Simple base class to support typed Tags for a simple object extensibility.
    /// </summary>
    public class TaggedObject : ITaggedObject
    {
        object _tag;

        /// <summary>
        /// Gets or sets a non null tag object of the specified type.
        /// </summary>
        /// <param name="type">The type of the tag to retrieve or sets.</param>
        /// <param name="newValue">Non null value of type <paramref name="type"/> to be set as the new tag value.</param>
        /// <returns>
        /// When <paramref name="newValue"/> is null, fhe first tag of the type, or null if no tag is the specified type.
        /// Otherwise, newValue is always returned.
        /// </returns>
        public object Tag( Type type, object newValue = null )
        {
            if( type == null ) throw new ArgumentNullException( "type" );
            if( newValue != null && !type.IsInstanceOfType( newValue ) ) throw new ArgumentException( "Invalid type/new object value." );
            if( _tag == null )
            {
                if( newValue != null )
                {
                    _tag = newValue is object[]? new object[] { newValue } : newValue;
                    return newValue;
                }
            }
            else
            {
                object[] a = _tag as object[];
                if( a == null )
                {
                    if( type.IsInstanceOfType( _tag ) )
                    {
                        return newValue != null ? _tag = newValue : _tag;
                    }
                    if( newValue != null )
                    {
                        _tag = new object[] { _tag, newValue };
                        return newValue;
                    }
                }
                else
                {
                    int i;
                    for( i = 0; i < a.Length; i++ )
                    {
                        object obj = a[i];
                        if( obj == null ) break;
                        if( type.IsInstanceOfType( obj ) )
                        {
                            return newValue != null ? a[i] = newValue : obj;
                        }
                        if( newValue != null )
                        {
                            if( i == a.Length )
                            {
                                Array.Resize( ref a, i * 2 );
                                _tag = a;
                            }
                            a[i] = newValue;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Gets or sets a non null tag object of the specified type..
        /// </summary>
        /// <typeparam name="T">The type of the tag to retrieve or set.</typeparam>
        /// <param name="newValue">Non null value to be set as the new tag value.</param>
        /// <returns>
        /// When <paramref name="newValue"/> is null, fhe first tag of the type, or null if no tag is the specified type.
        /// Otherwise, newValue is always returned.
        /// </returns>
        public T Tag<T>( T newValue = null ) where T : class => (T)Tag( typeof( T ), newValue );

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
