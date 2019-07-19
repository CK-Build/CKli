using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Implementation of a local <see cref="IWorldStore"/> in a directory on the file system.
    /// Worlds are stored as simple xml document that contains their description.
    /// Their state are stored (suffixed by ".State") in the world directory mapped ny a <see cref="IWorldLocalMapping"/>.
    /// </summary>
    public class LocalWorldStore : WorldStore
    {
        readonly NormalizedPath _directoryPath;

        public LocalWorldStore( string directoryPath, IWorldLocalMapping localWorldRootPathMapping )
            : base(localWorldRootPathMapping)
        {
            if( String.IsNullOrWhiteSpace( directoryPath ) ) throw new ArgumentNullException( nameof( directoryPath ) );
            _directoryPath = Path.GetFullPath( Environment.ExpandEnvironmentVariables( directoryPath ) );
        }

        LocalWorldName ToLocal( IWorldName w )
        {
            if( w is LocalWorldName loc ) return loc;
            throw new ArgumentException( "Must be a valid LocalWorldName.", nameof( w ) );
        }

        public override SharedWorldState GetOrCreateSharedState( IActivityMonitor m, IWorldName w )
        {
            throw new NotImplementedException();
        }

        protected override bool SaveSharedState( IActivityMonitor m, IWorldName w, XDocument d )
        {
            throw new NotImplementedException();
        }

        protected override IRootedWorldName DoCreateNew( IActivityMonitor m, string name, string parallelName, XDocument content )
        {
            Debug.Assert( !String.IsNullOrWhiteSpace( name ) );
            Debug.Assert( content != null );
            Debug.Assert( parallelName == null || !String.IsNullOrWhiteSpace( parallelName ) );

            string wName = name + (parallelName != null ? '[' + parallelName + ']' : String.Empty);
            var path = _directoryPath.AppendPart( wName + ".World.xml" );
            if( !File.Exists( path ) )
            {
                var w = new LocalWorldName( path, name, parallelName, LocalWorldRootPathMapping );
                if( !WriteWorldDescription( m, w, content ) )
                {
                    m.Error( $"Unable to create {wName} world." );
                    return null;
                }
                return w;
            }
            m.Error( $"World {wName} already exists." );
            return null;
        }

        public override IReadOnlyList<IRootedWorldName> ReadWorlds( IActivityMonitor m )
        {
            return Directory.GetFiles( _directoryPath, "*.World.xml" )
                                .Select( p => LocalWorldName.Parse( m, p, LocalWorldRootPathMapping ) )
                                .Where( w => w != null ).OrderBy( p => p.FullName )
                                .ToList();
        }

        public override XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w )
        {
            return XDocument.Load( ToLocal( w ).XmlDescriptionFilePath, LoadOptions.SetLineInfo );
        }

        public override bool WriteWorldDescription( IActivityMonitor m, IWorldName w, XDocument content )
        {
            if( content == null ) throw new ArgumentNullException( nameof( content ) );
            content.Save( ToLocal( w ).XmlDescriptionFilePath );
            return true;
        }
    }
}
