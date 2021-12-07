using CK.Core;

using Microsoft.Extensions.FileProviders;
using System.Xml.Linq;

namespace CK.Env.Plugin
{
    public static class GitFolderFilesExtension
    {
        /// <summary>
        /// Gets a <see cref="IFileInfo"/> in the <see cref="CurrentBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="fileName">The path and file name (root based).</param>
        /// <returns>The file info (null if it doesn't exist on the file system) and its path.</returns>
        public static (IFileInfo FileInfo, NormalizedPath Path) GetFileInfo( this GitRepository @this, string fileName )
        {
            var path = @this.SubPath
                               .AppendPart( "branches" ).AppendPart( @this.CurrentBranchName )
                               .Combine( fileName );
            var fileInfo = @this.FileSystem.GetFileInfo( path );
            if( !fileInfo.Exists || fileInfo.IsDirectory || fileInfo.PhysicalPath == null )
            {
                return (null, path);
            }
            return (fileInfo, path);
        }

        /// <summary>
        /// Reads a Xml document file in the <see cref="CurrentBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="fileName">The path and file name (root based).</param>
        /// <returns>The document and its path. Document is null if it can't be read and a fatal error is logged.</returns>
        public static (XDocument Doc, NormalizedPath Path) GetXmlDocument( this GitRepository @this, IActivityMonitor m, string fileName )
        {
            var (FileInfo, Path) = GetFileInfo( @this, fileName );
            return (FileInfo?.ReadAsXDocument(), Path);
        }

        /// <summary>
        /// Reads a <see cref="ITextFileInfo"/> the <see cref="CurrentBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="fileName">The path and file name (root based).</param>
        /// <returns>The text file info (null if it can't be read) and its path.</returns>
        public static (ITextFileInfo, NormalizedPath Path) GetTextFileInfo( this GitRepository @this, IActivityMonitor m, string fileName )
        {
            var (FileInfo, Path) = GetFileInfo( @this, fileName );
            return (FileInfo?.AsTextFileInfo( ignoreExtension: true ), Path);
        }

    }
}
