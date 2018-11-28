using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CK.Core;
using CK.Text;

namespace CK.Env
{
    /// <summary>
    /// Implementation of a local <see cref="IWorldStore"/> in a directory on the file system.
    /// Worlds are stored as simple xml document that contains their description.
    /// Their state are stored nearby them (suffixed by "-State").
    /// </summary>
    public class LocalWorldStore : IWorldStore
    {
        readonly NormalizedPath _directoryPath;

        public LocalWorldStore( string directoryPath )
        {
            _directoryPath = Path.GetFullPath( Environment.ExpandEnvironmentVariables( directoryPath ) );
        }

        LocalWorldName ToLocal( IWorldName w )
        {
            if( w is LocalWorldName loc ) return loc;
            throw new ArgumentException( "Must be a valid LocalWorldName.", nameof( w ) );
        }

        string ToStateFilePath( IWorldName w )
        {
            var p = ToLocal( w ).FilePath;
            p = p.Substring( 0, p.Length - 4 );
            return p + "-State.xml";
        }

        public IWorldName CreateNew( IActivityMonitor m, string name, string ltsKey, XDocument content )
        {
            if( String.IsNullOrWhiteSpace( name ) ) throw new ArgumentNullException( nameof( name ) );
            if( content == null ) throw new ArgumentNullException( nameof( content ) );
            if( String.IsNullOrWhiteSpace( ltsKey ) ) ltsKey = null;

            string wName = name + (ltsKey != null ? '-' + ltsKey : String.Empty);
            var path = _directoryPath.AppendPart( wName + "-World.xml" );
            if( !File.Exists( path ) ) 
            {
                var w = new LocalWorldName( path, name, ltsKey );
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

        public IReadOnlyList<IWorldName> ReadWorlds( IActivityMonitor m )
        {
            return Directory.GetFiles( _directoryPath, "*-World.xml" )
                                .Select( p => LocalWorldName.Parse( m, p ) )
                                .Where( w => w != null )
                                .ToList();
        }

        public RawXmlWorldState GetOrCreateLocalState( IActivityMonitor m, IWorldName w )
        {
            if( w == null ) throw new ArgumentNullException( nameof( w ) );
            var path = ToStateFilePath( w );
            if( File.Exists( path ) ) return new RawXmlWorldState( w, XDocument.Load( path ) );
            return new RawXmlWorldState( w );
        }

        public bool SetLocalState( IActivityMonitor m, RawXmlWorldState state )
        {
            if( state == null ) throw new ArgumentNullException( nameof( state ) );
            var path = ToStateFilePath( state.World );
            state.Document.Save( path );
            return true;
        }

        public XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w )
        {
            return XDocument.Load( ToLocal( w ).FilePath );
        }

        public bool WriteWorldDescription( IActivityMonitor m, IWorldName w, XDocument content )
        {
            if( content == null ) throw new ArgumentNullException( nameof( content ) );
            content.Save( ToLocal( w ).FilePath );
            return true;
        }
    }
}
