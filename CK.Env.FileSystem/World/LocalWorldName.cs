using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    public class LocalWorldName : WorldName
    {
        /// <summary>
        /// Gets the local file full path.
        /// </summary>
        public string FilePath { get; }

        internal LocalWorldName( string path, string worldName, string ltsKey )
            : base( worldName, ltsKey )
        {
            if( String.IsNullOrWhiteSpace( path ) ) throw new ArgumentNullException( nameof( path ) );
            FilePath = path;
        }

        internal static LocalWorldName Parse( IActivityMonitor m, string filePath )
        {
            Debug.Assert( filePath.EndsWith( "-World.xml" ) );
            try
            {
                var fName = Path.GetFileName( filePath );
                fName = fName.Substring( 0, fName.Length - 10 );
                int idx = fName.IndexOf( '-' );
                if( idx < 0 )
                {
                    return new LocalWorldName( filePath, fName, null );
                }
                return new LocalWorldName( filePath, fName.Substring( 0, idx ), fName.Substring( idx + 1 ) );
            }
            catch( Exception ex )
            {
                m.Error( $"While parsing file path: {filePath}.", ex );
                return null;
            }
        }
    }
}
