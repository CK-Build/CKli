using CK.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Basic implementation based on a text file of the <see cref="IWorldLocalMapping"/>.
    /// </summary>
    public class FileWorldLocalMapping : IWorldLocalMapping
    {
        readonly NormalizedPath _filePath;
        readonly Dictionary<string, NormalizedPath> _map;

        public FileWorldLocalMapping( in NormalizedPath filePath )
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
                    _map[kv.Item1] = Path.GetFullPath( kv.Item2 );
                }
            }
        }

        /// <summary>
        /// Fires when <see cref="SetMap"/> changed a mapping (and has persisted the change).
        /// </summary>
        public event EventHandler? MappingChanged;

        /// <summary>
        /// Always true. 
        /// </summary>
        public bool CanSetMapping => true;

        /// <summary>
        /// Gets the root path for a World.
        /// If the <see cref="IWorldName.FullName"/> is defined, the mapped path is taken as-is.
        /// Otherwise, on a parallel world and if the Stack name is mapped (the default world),
        /// we map the parallel world next to the default one.
        /// </summary>
        /// <param name="w">The world name.</param>
        /// <returns>The path to the root directory or a <see cref="NormalizedPath.IsEmptyPath"/> if it is not mapped.</returns>
        public NormalizedPath GetRootPath( IWorldName w )
        {
            if( !_map.TryGetValue( w.FullName, out NormalizedPath p ) )
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

        /// <summary>
        /// Creates or updates a mapping between a <see cref="IWorldName.FullName"/> and a local path.
        /// The change is immediately persisted.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="worldFullName">World's full name. Must not be null, empty or white space.</param>
        /// <param name="mappedPath">Local path. Must be rooted.</param>
        /// <returns>True if the path has been set, false if nothing changed.</returns>
        public bool SetMap( IActivityMonitor m, string worldFullName, in NormalizedPath mappedPath )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( worldFullName );
            Throw.CheckArgument( mappedPath.IsRooted );

            if( _map.TryGetValue( worldFullName, out var exists )
                && (exists == mappedPath || mappedPath.IsEmptyPath) )
            {
                m.Trace( $"Mapping not changed: '{worldFullName}' -> '{exists}'." );
                return false;
            }
            _map[worldFullName] = mappedPath;
            m.Info( $"Mapping updated: '{worldFullName}' -> '{mappedPath}'." );
            Save();
            MappingChanged?.Invoke( this, EventArgs.Empty );
            return true;
        }

        void Save()
        {
            File.WriteAllLines( _filePath, _map.OrderBy( k => k.Key ).Select( k => k.Key + " > " + k.Value ) );
        }

        /// <summary>
        /// Tries to find a <see cref="IRootedWorldName"/> from its mapped path (any path below).
        /// </summary>
        /// <param name="path">The path to challenge.</param>
        /// <returns>The rooted world name.</returns>
        public IRootedWorldName? ReverseMap( in NormalizedPath path )
        {
            Throw.CheckArgument( !path.IsEmptyPath );
            // We lookup the path and use GetRootPath to also find "virtual" mapping for parallel worlds.
            // This is safe since we check that the GetRootPath of the candidate is exactly the current path.
            var p = path;
            do
            {
                if( WorldName.TryParse( p.LastPart, out var candidate ) )
                {
                    if( GetRootPath( candidate ) == p )
                    {
                        return new RootedWorldName( candidate.Name, candidate.ParallelName, p );
                    }
                }
                p = p.RemoveLastPart();
            }
            while( !p.IsEmptyPath );
            return null;
        }

    }

}
