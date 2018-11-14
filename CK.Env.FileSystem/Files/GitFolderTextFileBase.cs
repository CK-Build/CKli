using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Env
{
    /// <summary>
    /// Basic base class for text files.
    /// </summary>
    public abstract class GitFolderTextFileBase
    {
        ITextFileInfo _file;

        protected GitFolderTextFileBase( GitFolder f, NormalizedPath path )
        {
            Folder = f;
            Path = f.SubPath
                    .AppendPart( "branches" ).AppendPart( f.CurrentBranchName )
                    .Combine( path );
        }

        ITextFileInfo GetFile() => _file ?? (_file = Folder.FileSystem.GetFileInfo( Path ).AsTextFileInfo( ignoreExtension: true ));

        /// <summary>
        /// Gets the Git folder.
        /// </summary>
        public GitFolder Folder { get; }

        /// <summary>
        /// Gets the path in the <see cref="GitFolder.FileSystem"/> root.
        /// </summary>
        public NormalizedPath Path { get; }

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
                Folder.FileSystem.Delete( m, Path );
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
            if( !Folder.FileSystem.CopyTo( m, newContent, Path ) )
            {
                return false;
            }
            _file = null;
            return true;
        }

    }
}
