using CK.Core;
using System;

namespace CK.Env.DependencyModel
{
    /// <summary>
    /// Simple base class to support typed Tags for a simple object extensibility.
    /// </summary>
    public class TaggedObject : ITaggedObject
    {
        object? _tag;

        /// <inheritdoc />
        public object? Tag( Type type, object? newValue = null )
        {
            Throw.CheckNotNullArgument( type );
            Throw.CheckArgument( newValue == null || type.IsInstanceOfType( newValue ) );
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
                object[]? a = _tag as object[];
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
            return null;
        }

        /// <inheritdoc />
        public T? Tag<T>( T? newValue = null ) where T : class => (T?)Tag( typeof( T ), newValue );

        /// <inheritdoc />
        public bool RemoveTags( Type type )
        {
            Throw.CheckNotNullArgument( type );
            if( _tag != null )
            {
                object?[]? a = _tag as object?[];
                if( a == null )
                {
                    if( type.IsInstanceOfType( _tag ) )
                    {
                        _tag = null;
                        return true;
                    }
                    return false;
                }
                int i = 0, j = 0;
                while( i < a.Length )
                {
                    object? obj = a[i];
                    if( obj == null ) break;
                    if( !type.IsInstanceOfType( obj ) ) a[j++] = obj;
                    i++;
                }
                if( j == 0 )
                {
                    _tag = null;
                    return true;
                }
                else if( j < i )
                {
                    do
                    {
                        a[j++] = null;
                    }
                    while( j < i );
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc />
        public bool RemoveTags<T>() where T : class => RemoveTags( typeof( T ) );

    }
}
