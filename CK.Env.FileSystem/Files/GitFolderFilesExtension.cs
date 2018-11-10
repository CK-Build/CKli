using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CK.Env
{
    public static class GitFolderFilesExtension
    {
        /// <summary>
        /// Reads a Xml document file in the <see cref="CurrentBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="fileName">The path and file name (root based).</param>
        /// <returns>The document and its path. Document is null if it can't be read and a fatal error is logged.</returns>
        public static (XDocument Doc, NormalizedPath Path) GetXmlDocument( this GitFolder @this, IActivityMonitor m, string fileName )
        {
            var pathXml = @this.SubPath
                               .AppendPart( "branches" ).AppendPart( @this.CurrentBranchName )
                               .Combine( fileName );
            var rXml = @this.FileSystem.GetFileInfo( pathXml );
            if( !rXml.Exists || rXml.IsDirectory || rXml.PhysicalPath == null )
            {
                m.Fatal( $"{pathXml} must exist." );
                return (null, pathXml);
            }
            return (rXml.ReadAsXDocument(), pathXml);
        }

    }
}
