using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Immmutable encapsulation of a path that normalizes <see cref="System.IO.Path.AltDirectorySeparatorChar"/>
    /// to <see cref="System.IO.Path.DirectorySeparatorChar"/>.
    /// </summary>
    public struct NormalizedPath : IEquatable<NormalizedPath>, IComparable<NormalizedPath>
    {
        static readonly char[] _separators = new[] { System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar };

        readonly string[] _parts;
        readonly string _path;

        public NormalizedPath( string path )
        {
            _parts = path?.Split( _separators, StringSplitOptions.RemoveEmptyEntries );
            if( _parts != null && _parts.Length == 0 ) _parts = null;
            _path = _parts?.Concatenate( FileUtil.DirectorySeparatorString );
        }

        NormalizedPath( string[] parts, string path )
        {
            _parts = parts;
            _path = path;
        }

        public NormalizedPath Combine( NormalizedPath suffix )
        {
            if( IsEmpty ) return suffix;
            if( suffix.IsEmpty ) return this;
            var parts = new string[_parts.Length + suffix._parts.Length];
            Array.Copy( _parts, parts, _parts.Length );
            Array.Copy( suffix._parts, 0, parts, _parts.Length, suffix._parts.Length );
            return new NormalizedPath( parts, _path + System.IO.Path.DirectorySeparatorChar + suffix._path );
        }

        public string LastPart => _parts?[_parts.Length - 1] ?? String.Empty;

        public string FirstPart => _parts?[0] ?? String.Empty;

        public NormalizedPath AppendPart( string part )
        {
            if( string.IsNullOrEmpty( part ) ) throw new ArgumentNullException( nameof( part ) );
            if( part.IndexOfAny( _separators ) >= 0 ) throw new ArgumentException( $"Illegal separators in '{part}'.", nameof( part ) );
            if( _parts == null ) return new NormalizedPath( new[] { part }, part );
            var parts = new string[_parts.Length + 1];
            Array.Copy( _parts, parts, _parts.Length );
            parts[_parts.Length] = part;
            return new NormalizedPath( parts, _path + System.IO.Path.DirectorySeparatorChar + part );
        }

        public NormalizedPath RemoveLastPart()
        {
            if( _parts == null || _parts.Length == 1 ) return new NormalizedPath();
            var parts = new string[_parts.Length - 1];
            Array.Copy( _parts, parts, parts.Length );
            return new NormalizedPath( parts, _path.Substring( 0, _path.Length - _parts[_parts.Length - 1].Length - 1 ) );
        }

        public NormalizedPath RemoveFirstPart()
        {
            if( _parts == null || _parts.Length == 1 ) return new NormalizedPath();
            var parts = new string[_parts.Length - 1];
            Array.Copy( _parts, 1, parts, 0, parts.Length );
            return new NormalizedPath( parts, _path.Substring( _parts[0].Length + 1 ) );
        }

        public NormalizedPath RemovePart( int index ) => RemoveParts( index, 1 );

        public NormalizedPath RemoveParts( int startIndex, int count )
        {
            int to = startIndex + count;
            if( _parts == null || startIndex < 0 || to > _parts.Length ) throw new IndexOutOfRangeException();
            int nb = _parts.Length - count;
            if( nb == 0 ) return new NormalizedPath();
            var parts = new string[nb];
            Array.Copy( _parts, parts, startIndex );
            int sIdx = startIndex, sLen = count;
            int tailCount = _parts.Length - to;
            if( tailCount != 0 ) Array.Copy( _parts, to, parts, startIndex, tailCount );
            else --sIdx;
            int i = 0;
            for( ; i < startIndex; ++i ) sIdx += _parts[i].Length;
            for( ; i < to; ++i ) sLen += _parts[i].Length;
            return new NormalizedPath( parts, _path.Remove( sIdx, sLen ) );
        }

        public bool StartsWith( NormalizedPath other, bool strict = true ) => !other.IsEmpty
                                                            && !IsEmpty
                                                            && other._parts.Length <= _parts.Length
                                                            && (!strict || other._parts.Length < _parts.Length)
                                                            && StringComparer.OrdinalIgnoreCase.Equals( other.LastPart, _parts[other._parts.Length-1] ) 
                                                            && _path.StartsWith( other._path, StringComparison.OrdinalIgnoreCase );

        public NormalizedPath RemovePrefix( NormalizedPath prefix )
        {
            if( !StartsWith( prefix, false ) ) throw new ArgumentException( $"'{prefix}' is not a prefix of '{_path}'." );
            int nb = _parts.Length - prefix._parts.Length;
            if( nb == 0 ) return new NormalizedPath();
            var parts = new string[nb];
            Array.Copy( _parts, prefix._parts.Length, parts, 0, nb );
            return new NormalizedPath( parts, _path.Substring( prefix._path.Length + 1 ) );
        }

        public bool IsEmpty => _parts == null;

        public IReadOnlyList<string> Parts => _parts ?? Array.Empty<string>();

        public string Path => _path ?? String.Empty;

        public int CompareTo( NormalizedPath other ) => StringComparer.OrdinalIgnoreCase.Compare( _path, other._path );

        public override bool Equals( object obj ) => obj is NormalizedPath p && Equals( p );

        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode( _path );

        public bool Equals( NormalizedPath other ) => _path.Equals( other._path );

        public override string ToString() => _path ?? String.Empty;

        public string ToString( char separator )
        {
            if( _path == null ) return String.Empty;
            if( separator == System.IO.Path.DirectorySeparatorChar || _parts.Length == 1 )
            {
                return _path;
            }
            return _path.Replace( System.IO.Path.DirectorySeparatorChar, separator );
        }
    }
}
