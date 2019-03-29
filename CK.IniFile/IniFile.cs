using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CK.IniFile
{
    class IniFile<T> : ICollection<T>
        where T : IniLine
    {
        readonly List<T> _lines;
        public T this[int index] { get => _lines[index]; set => _lines[index] = value; }

        public int Count => _lines.Count;

        IniFile( IniFormat format )
        {
            Format = format;
            _lines = new List<T>();
        }
        public IniFormat Format { get; }

        public bool IsReadOnly => false;

        

        public static IniFile<T> FromText( string text, IniFormat format )
        {
            if( format.SupportSection )
            {
                throw new NotImplementedException();
            }
            var output = new IniFile<T>( format );
            string[] lines = Regex.Split( text, "\r\n|\r|\n" );
            for( int i = 0; i < lines.Length; i++ )
            {
                
            }
            return output;
        }

        public void Add( T item )
        {
            if( Format.Duplication != IniDuplication.Allowed && _lines.Any( p => p.Key == item.Key ) )
            {
                throw new InvalidOperationException( "Format does not allow duplicate Key." );
            }
            _lines.Add( item );
        }

        public void Clear() => _lines.Clear();


        public bool Contains( T item ) => _lines.Contains( item );


        public void CopyTo( T[] array, int arrayIndex ) => _lines.CopyTo( array, arrayIndex );


        public bool Remove( T item ) => _lines.Remove( item );

        public int RemoveByKey( string key )
        {
            return _lines.RemoveAll( p => p.Key == key );
        }

        public IEnumerator<T> GetEnumerator() => _lines.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _lines.GetEnumerator();
    }
}
