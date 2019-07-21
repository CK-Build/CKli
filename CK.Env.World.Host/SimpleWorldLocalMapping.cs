using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Basic implementation based on a text file of the <see cref="IWorldLocalMapping"/>.
    /// </summary>
    public class SimpleWorldLocalMapping : IWorldLocalMapping
    {
        readonly NormalizedPath _filePath;
        readonly Dictionary<string, NormalizedPath> _map;

        public SimpleWorldLocalMapping( in NormalizedPath filePath )
        {
            _filePath = filePath;
            _map = new Dictionary<string, NormalizedPath>( StringComparer.OrdinalIgnoreCase );
            if( File.Exists( _filePath ) )
            {
                foreach( var kv in File.ReadAllLines( _filePath )
                        .Select( line => line.Split( '>' ) )
                        .Where( p => p.Length == 2 )
                        .Select( p => (p[0].Trim(), p[1].Trim()) )
                        .Where( p => p.Item1.Length > 0 && p.Item2.Length > 0 ) )
                {
                    _map[kv.Item1] = kv.Item2;
                }
            }
        }

        protected void Save()
        {
            var text = _map.OrderBy( k => k.Key ).Select( k => k.Key + " > " + k.Value ).Concatenate( Environment.NewLine );
            File.WriteAllText( _filePath, text );
        }

        /// <summary>
        /// Gets the root path for a World.
        /// If the <see cref="IWorldName.FullName"/> is defined, the mapped path is taken as-is.
        /// Otherwise, on a parallel world and if the if the Stack name is mapped (the default world),
        /// we map the parallel world next to the default one.
        /// </summary>
        /// <param name="w">The world name.</param>
        /// <returns>The path to the root directory or null if it is not mapped.</returns>
        public NormalizedPath GetRootPath( IWorldName w )
        {
            NormalizedPath p;
            if( !_map.TryGetValue( w.FullName, out p ) )
            {
                // If Name is not the same as FullName, we are on a parallel
                // world that is not mapped: if the Stack name is mapped (the default world),
                // we map the parallel world next to the default one.
                if( _map.TryGetValue( w.Name, out p ) )
                {
                    p = p.RemoveLastPart().AppendPart( w.FullName );
                }
            }
            if( !p.IsEmptyPath )
            {
                Directory.CreateDirectory( p );
                File.WriteAllText( p.AppendPart( "CKli-World.htm" ), "<html></html>" );
            }
            return p;
        }

        internal bool IsMapped( string worldFullName ) => _map.ContainsKey( worldFullName );

        internal void SetMap( string worldFullName, NormalizedPath mappedPath )
        {
            Debug.Assert( mappedPath.IsRooted );
            _map[worldFullName] = mappedPath;
        }
    }

}
