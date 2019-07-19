using CK.Core;
using CK.Text;
using System;
using System.Diagnostics;
using System.IO;

namespace CK.Env
{
    /// <summary>
    /// Local world name
    /// </summary>
    public class LocalWorldName : WorldName, IRootedWorldName
    {
        internal LocalWorldName( string xmlDescriptionFilePath, string worldName, string parallelName, IWorldLocalMapping localMap )
            : base( worldName, parallelName )
        {
            if( String.IsNullOrWhiteSpace( xmlDescriptionFilePath ) ) throw new ArgumentNullException( nameof( xmlDescriptionFilePath ) );
            XmlDescriptionFilePath = xmlDescriptionFilePath;
            Root = localMap.GetRootPath( this );
        }

        /// <summary>
        /// Gets the local file full path.
        /// </summary>
        public string XmlDescriptionFilePath { get; }

        /// <summary>
        /// Gets the local world root directory path.
        /// This is <see cref="NormalizedPath.IsEmptyPath"/> if the world is not mapped by the <see cref="IWorldLocalMapping"/>.
        /// </summary>
        public NormalizedPath Root { get; }

        internal static LocalWorldName Parse( IActivityMonitor m, string filePath, IWorldLocalMapping localMap )
        {
            Debug.Assert( filePath.EndsWith( ".World.xml" ) );
            try
            {
                var fName = Path.GetFileName( filePath );
                Debug.Assert( ".World.xml".Length == 10 );
                fName = fName.Substring( 0, fName.Length - 10 );
                int idx = fName.IndexOf( '[' );
                if( idx < 0 )
                {
                    return new LocalWorldName( filePath, fName, null, localMap );
                }
                int paraLength = fName.IndexOf( ']' ) - idx - 1;
                return new LocalWorldName( filePath, fName.Substring( 0, idx ), fName.Substring( idx + 1, paraLength ), localMap );
            }
            catch( Exception ex )
            {
                m.Error( $"While parsing file path: {filePath}.", ex );
                return null;
            }
        }
    }
}
