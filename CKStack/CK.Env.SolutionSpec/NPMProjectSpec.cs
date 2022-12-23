using CK.Core;
using System;
using System.IO;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Immutable NPM project specification.
    /// </summary>
    public class NPMProjectSpec : INPMProjectSpec
    {
        internal NPMProjectSpec( XElementReader r )
        {
            Folder = new NormalizedPath( r.HandleRequiredAttribute<string>( nameof( Folder ) ) ).With( NormalizedPathRootKind.None );
            PackageName = HandlePackageName( r.HandleOptionalAttribute( nameof( PackageName ), "" ) );
            OutputFolder = r.HandleRequiredAttribute<string>( nameof( OutputFolder ) );
            if( Folder.IsRooted ) throw new InvalidDataException( "Path cannot be rooted." );
            if( OutputFolder.IsRooted ) throw new InvalidDataException( "Path cannot be rooted." );
        }


        public NPMProjectSpec( NormalizedPath folderPath, string packageName )
        {
            Folder = folderPath;
            PackageName = HandlePackageName( packageName );
        }

        string HandlePackageName( string attributeValue )
        {
            if( !String.IsNullOrWhiteSpace( attributeValue ) )
            {
                return attributeValue;
            }
            if( Folder.IsEmptyPath )
            {
                throw new Exception( "NPMProject specified as a public root project (Folder attribute is missing, empty or '/') must specify a PackageName or IsPrivate must be true." );
            }
            return Folder.LastPart.ToLowerInvariant();
        }

        /// <summary>
        /// Gets the package name.
        /// </summary>
        public string PackageName { get; }

        /// <summary>
        /// Gets the folder path relative to the solution root.
        /// </summary>
        public NormalizedPath Folder { get; }

        /// <summary>
        /// Gets the folder where the project build output.
        /// </summary>
        public NormalizedPath OutputFolder { get; }
    }
}
