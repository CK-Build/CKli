using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env.MSBuild
{
    /// <summary>
    /// Describes an expected NPM project as described in a World.
    /// </summary>
    public class NpmProjectDescription
    {
        /// <summary>
        /// Initializes a new <see cref="NpmProjectDescription"/>.
        /// </summary>
        /// <param name="folderRelativePath">Relative path to the NPM project folder from the container solution folder.</param>
        /// <param name="isPrivate">Whether the project must be private (ie. not published).</param>
        public NpmProjectDescription( NormalizedPath folderRelativePath, bool isPrivate = false )
        {
            Folder = folderRelativePath;
            IsPrivate = IsPrivate;
        }

        /// <summary>
        /// Initializes a new Npm project instance from a Xml element.
        /// </summary>
        /// <param name="s">The holding solution.</param>
        /// <param name="e">Xml element with required Path and optional IsPrivate="False" attributes.</param>
        public NpmProjectDescription( XElement e )
        {
            Folder = e.AttributeRequired( "Path" ).Value;
            IsPrivate = (bool?)e.Attribute( "IsPrivate" ) ?? false;
        }

        public NormalizedPath Folder { get; }

        public bool IsPrivate { get; }

    }
}
