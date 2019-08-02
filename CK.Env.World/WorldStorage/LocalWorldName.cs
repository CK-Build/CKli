using CK.Core;
using CK.Text;
using System;
using System.Diagnostics;
using System.IO;

namespace CK.Env
{
    /// <summary>
    /// Local world name is a <see cref="IRootedWorldName"/> that also carries
    /// its definition file path (<see cref="XmlDescriptionFilePath"/>).
    /// </summary>
    public class LocalWorldName : WorldName, IRootedWorldName
    {
        /// <summary>
        /// Initializes a new <see cref="LocalWorldName"/>.
        /// </summary>
        /// <param name="xmlDescriptionFilePath">Path of the definition file.</param>
        /// <param name="stackName">The stack name.</param>
        /// <param name="parallelName">The optional parallel name. Can be null or empty.</param>
        /// <param name="localMap">Required to compute the <see cref="Root"/> folder.</param>
        public LocalWorldName( NormalizedPath xmlDescriptionFilePath, string stackName, string parallelName, IWorldLocalMapping localMap )
            : base( stackName, parallelName )
        {
            if( String.IsNullOrWhiteSpace( xmlDescriptionFilePath ) ) throw new ArgumentNullException( nameof( xmlDescriptionFilePath ) );
            XmlDescriptionFilePath = xmlDescriptionFilePath;
            Root = localMap.GetRootPath( this );
        }

        /// <summary>
        /// Gets the local definition file full path.
        /// </summary>
        public NormalizedPath XmlDescriptionFilePath { get; }

        /// <summary>
        /// Gets the local world root directory path.
        /// This is <see cref="NormalizedPath.IsEmptyPath"/> if the world is not mapped.
        /// </summary>
        public NormalizedPath Root { get; }

        /// <summary>
        /// Tries to parse a xml World definition file name.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="path">The file path.</param>
        /// <param name="localMap">The mapper to use.</param>
        /// <returns>The name or null on error.</returns>
        public static LocalWorldName TryParse( IActivityMonitor m, NormalizedPath path, IWorldLocalMapping localMap )
        {
            if( path.IsEmptyPath || !path.LastPart.EndsWith( ".World.xml", StringComparison.OrdinalIgnoreCase ) )
            {
                m.Error( $"Path must end with '.World.xml': '{path}'" );
                return null;
            }
            try
            {
                var fName = path.LastPart;
                Debug.Assert( ".World.xml".Length == 10 );
                fName = fName.Substring( 0, fName.Length - 10 );
                int idx = fName.IndexOf( '[' );
                if( idx < 0 )
                {
                    return new LocalWorldName( path, fName, null, localMap );
                }
                int paraLength = fName.IndexOf( ']' ) - idx - 1;
                return new LocalWorldName( path, fName.Substring( 0, idx ), fName.Substring( idx + 1, paraLength ), localMap );
            }
            catch( Exception ex )
            {
                m.Error( $"While parsing file path: {path}.", ex );
                return null;
            }
        }
    }
}