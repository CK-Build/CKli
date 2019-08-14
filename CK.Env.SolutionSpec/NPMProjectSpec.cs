using CK.Core;
using CK.Text;
using System;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Immutable NPM project specification.
    /// </summary>
    public class NPMProjectSpec : INPMProjectSpec
    {
        internal NPMProjectSpec( XElementReader r  )
        {
            IsPrivate = r.HandleOptionalAttribute( nameof( IsPrivate ), false );
            Folder =  new NormalizedPath(r.HandleRequiredAttribute<string>( nameof( Folder ))).With( NormalizedPathRootKind.None );
            PackageName = r.HandleRequiredAttribute<string>( nameof( PackageName ) );
            if( String.IsNullOrWhiteSpace( PackageName ) )
            {
                if( Folder.IsEmptyPath && !IsPrivate )
                {
                    throw new Exception( "NPMProject specified as a public root project (Folder attribute is missing, empty or '/') must specify a PackageName or IsPrivate must be true." );
                }
                PackageName = Folder.LastPart.ToLowerInvariant();
            }
        }

        /// <summary>
        /// Gets the package name.
        /// This can be null if <see cref="IsPrivate"/> is true.
        /// </summary>
        public string PackageName { get; }

        /// <summary>
        /// Gets whether this package must be private (ie. not published).
        /// </summary>
        public bool IsPrivate { get; }

        /// <summary>
        /// Gets the folder path relative to the solution root.
        /// </summary>
        public NormalizedPath Folder { get; }

    }
}
