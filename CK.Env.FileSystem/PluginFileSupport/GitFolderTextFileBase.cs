using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env.Plugins
{
    /// <summary>
    /// Basic base class for text files.
    /// </summary>
    public abstract class GitFolderTextFileBase
    {
        ITextFileInfo _file;

        protected GitFolderTextFileBase( GitFolder f, NormalizedPath filePath )
        {
            if( !filePath.StartsWith( f.SubPath ) ) throw new ArgumentException( $"Path {filePath} must start with folder {f.SubPath}." );
            Folder = f;
            FilePath = filePath;
        }

        ITextFileInfo GetFile() => _file ?? (_file = Folder.FileSystem.GetFileInfo( FilePath ).AsTextFileInfo( ignoreExtension: true ));

        /// <summary>
        /// Gets the Git folder.
        /// </summary>
        public GitFolder Folder { get; }

        /// <summary>
        /// Gets the path in the <see cref="GitFolder.FileSystem"/> root.
        /// </summary>
        public NormalizedPath FilePath { get; }

        /// <summary>
        /// Gets the text file content or null if it does not exist.
        /// To update this content, <see cref="CreateOrUpdate(IActivityMonitor, string)"/> must
        /// be used.
        /// </summary>
        public string TextContent => GetFile()?.TextContent;

        /// <summary>
        /// Deletes the file if it exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void Delete( IActivityMonitor m )
        {
            if( GetFile() != null )
            {
                Folder.FileSystem.Delete( m, FilePath );
                _file = null;
            }
            OnDeleted( m );
        }

        /// <summary>
        /// Called by <see cref="Delete"/> once <see cref="TextContent"/> is null
        /// and the file has been deleted from the file system.
        /// </summary>
        /// <param name="m"></param>
        protected virtual void OnDeleted( IActivityMonitor m )
        {
        }

        /// <summary>
        /// Saves (creates or updates), this file with a new content or calls <see cref="Delete"/>
        /// if content is null.
        /// If the content is the same as the current <see cref="TextContent"/> the save is skipped.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="newContent">The content. Null to delete the file.</param>
        /// <returns>True on success, false on error.</returns>
        protected bool CreateOrUpdate( IActivityMonitor m, string newContent )
        {
            string oldContent = GetFile()?.TextContent;
            if( oldContent == newContent ) return true;
            if( newContent == null )
            {
                Delete( m );
                return true;
            }
            if( !Folder.FileSystem.CopyTo( m, newContent, FilePath ) )
            {
                return false;
            }
            _file = null;
            return true;
        }

    }
}
