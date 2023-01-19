using CK.Core;

using System;
using System.Diagnostics;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Local world name is a <see cref="RootedWorldName"/> that also carries
    /// its definition file path (<see cref="XmlDescriptionFilePath"/>) and whether this file
    /// is available or not. 
    /// </summary>
    public sealed class LocalWorldName : RootedWorldName, IRootedWorldName
    {
        /// <summary>
        /// Initializes a new <see cref="LocalWorldName"/>.
        /// </summary>
        /// <param name="xmlDescriptionFilePath">Path of the definition file.</param>
        /// <param name="stackName">The stack name.</param>
        /// <param name="parallelName">The optional parallel name. Can be null or empty.</param>
        /// <param name="localMap">Required to compute the <see cref="Root"/> folder.</param>
        public LocalWorldName( NormalizedPath xmlDescriptionFilePath, string stackName, string? parallelName, IWorldLocalMapping localMap )
            : base( stackName, parallelName, localMap )
        {
            Throw.CheckArgument( !xmlDescriptionFilePath.IsEmptyPath );
            XmlDescriptionFilePath = xmlDescriptionFilePath;
        }

        /// <summary>
        /// Initializes a new <see cref="LocalWorldName"/>.
        /// </summary>
        /// <param name="stackName">The stack name.</param>
        /// <param name="parallelName">The optional parallel name. Can be null or empty.</param>
        /// <param name="rootPath">Root folder of world.</param>
        /// <param name="xmlDescriptionFilePath">Path of the definition file.</param>
        public LocalWorldName( string stackName, string? parallelName, NormalizedPath rootPath, NormalizedPath xmlDescriptionFilePath )
            : base( stackName, parallelName, rootPath )
        {
            Throw.CheckArgument( !Root.IsEmptyPath );
            XmlDescriptionFilePath = xmlDescriptionFilePath;
            HasDefinitionFile = true;
        }

        /// <summary>
        /// Gets the local definition file full path.
        /// </summary>
        public NormalizedPath XmlDescriptionFilePath { get; }

        /// <summary>
        /// Gets or sets whether the xml definition file for this stack exists.
        /// </summary>
        public bool HasDefinitionFile { get; set; }

        /// <summary>
        /// Tries to parse a xml World definition file path of the form "...StackName/.[Public|Private]Stack/StackName[parallel name].World.xml".
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>The name or null on error.</returns>
        public static LocalWorldName? TryParseDefinitionFilePath( NormalizedPath path )
        {
            if( path.IsEmptyPath
                || path.Parts.Count < 4
                || !path.LastPart.EndsWith( ".World.xml", StringComparison.OrdinalIgnoreCase ) ) return null;
            var fName = path.LastPart;
            Debug.Assert( ".World.xml".Length == 10 );
            fName = fName.Substring( 0, fName.Length - 10 );
            if( !TryParse( fName, out var stackName, out var parallelName ) ) return null;
            if( stackName != path.Parts[^3] ) return null;
            var wRoot = path.RemoveLastPart( 2 );
            if( parallelName != null )
            {
                return new LocalWorldName( stackName, parallelName, wRoot.AppendPart($"[{parallelName}]"), path );
            }
            return new LocalWorldName( stackName, null, wRoot, path );
        }

        /// <summary>
        /// Tries to parse a xml World definition file name (must end with ".World.xml").
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="localMap">The mapper to use.</param>
        /// <returns>The name or null on error.</returns>
        public static LocalWorldName? TryParseOBSOLETE( NormalizedPath path, IWorldLocalMapping localMap )
        {
            if( path.IsEmptyPath || !path.LastPart.EndsWith( ".World.xml", StringComparison.OrdinalIgnoreCase ) ) return null;
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
            catch
            {
                return null;
            }
        }

    }
}
