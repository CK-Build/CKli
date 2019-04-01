using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CK.IniFile
{
    public class IniFile<TFormat, TLine> : ICollection<TLine>
        where TFormat : IniFormat<TLine>
        where TLine : IniLine
    {
        readonly List<TLine> _lines;
        public TLine this[int index] { get => _lines[index]; set => _lines[index] = value; }

        public int Count => _lines.Count;
        /// <summary>
        /// Create a new IniFile.
        /// </summary>
        /// <param name="format"></param>
        public IniFile( TFormat format )
        {
            Format = format;
            _lines = new List<TLine>();
        }
        public TFormat Format { get; }

        public bool IsReadOnly => false;

        public static IniFile<TFormat, TLine> FromText( string text, TFormat format )
        {
            if( format.SupportSection )
            {
                throw new NotImplementedException();
            }
            var output = new IniFile<TFormat,TLine>( format );
            string[] lines = Regex.Split( text, "\r\n|\r|\n" );
            for( int i = 0; i < lines.Length; i++ )
            {
                format.ParseLine( lines[i] );
            }
            return output;
        }
        /// <summary>
        /// It will also remove duplicate entries
        /// </summary>
        /// <param name="line"></param>
        public void EnsureLine(TLine line)
        {
            _lines.RemoveAll( p => p.Key == line.Key );
            _lines.Add( line );
        }

        public void Add( TLine item )
        {
            if( Format.Duplication != IniDuplication.Allowed && _lines.Any( p => p.Key == item.Key ) )
            {
                throw new InvalidOperationException( "Format does not allow duplicate Key." );
            }
            _lines.Add( item );
        }

        public void Clear() => _lines.Clear();


        public bool Contains( TLine item ) => _lines.Contains( item );


        public void CopyTo( TLine[] array, int arrayIndex ) => _lines.CopyTo( array, arrayIndex );


        public bool Remove( TLine item ) => _lines.Remove( item );

        public int RemoveByKey( string key )
        {
            return _lines.RemoveAll( p => p.Key == key );
        }

        public IEnumerator<TLine> GetEnumerator() => _lines.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _lines.GetEnumerator();
        public override string ToString()
        {
            return string.Join( "\n", _lines.Select( p => p.ToString<TFormat, TLine>( Format ) ));
        }
    }
}
