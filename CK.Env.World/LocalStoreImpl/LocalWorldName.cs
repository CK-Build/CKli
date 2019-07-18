using CK.Core;
using System;
using System.Diagnostics;
using System.IO;

namespace CK.Env
{
    public class LocalWorldName : WorldName
    {
        /// <summary>
        /// Gets the local file full path.
        /// </summary>
        public string XmlDescriptionFilePath { get; }

        /// <summary>
        /// Gets the local worl root directory path.
        /// </summary>
        public string RootDirectoryPath { get; }

        internal LocalWorldName( string xmlDescriptionFilePath, string worldName, string parallelName, ILocalWorldRootPathMapping localMap )
            : base( worldName, parallelName )
        {
            if( String.IsNullOrWhiteSpace( xmlDescriptionFilePath ) ) throw new ArgumentNullException( nameof( xmlDescriptionFilePath ) );
            XmlDescriptionFilePath = xmlDescriptionFilePath;
            RootDirectoryPath = localMap.GetRootPath( this );
        }

        internal static LocalWorldName Parse( IActivityMonitor m, string filePath, ILocalWorldRootPathMapping localMap )
        {
            Debug.Assert( filePath.EndsWith( ".World.xml" ) );
            try
            {
                var fName = Path.GetFileName( filePath );
                fName = fName.Substring( 0, fName.Length - 10 );//remove .World.xml
                int idx = fName.IndexOf( '[' );
                if( idx < 0 )
                {
                    return new LocalWorldName( filePath, fName, null, localMap );
                }
                int ltsLength = fName.IndexOf( ']' ) - idx - 1;
                return new LocalWorldName( filePath, fName.Substring( 0, idx ), fName.Substring( idx + 1, ltsLength ), localMap );
            }
            catch( Exception ex )
            {
                m.Error( $"While parsing file path: {filePath}.", ex );
                return null;
            }
        }
    }
}