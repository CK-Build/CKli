using CK.Core;
using CK.Text;

namespace CK.Env
{
    /// <summary>
    /// Basic cache of a text file content with helpers to manage it (update, create or delete).
    /// </summary>
    public abstract class TextFileBase
    {
        ITextFileInfo _file;

        protected TextFileBase( FileSystem fs, NormalizedPath filePath )
        {
            FileSystem = fs;
            FilePath = filePath;
        }

        ITextFileInfo GetFile() => _file ?? (_file = FileSystem.GetFileInfo( FilePath ).AsTextFileInfo( ignoreExtension: true ));

        /// <summary>
        /// Gets the path in the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath FilePath { get; }

        /// <summary>
        /// Gets the <see cref="FileSystem"/>.
        /// </summary>
        public FileSystem FileSystem { get; }

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
                FileSystem.Delete( m, FilePath );
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
        /// <param name="forceSave">True to always save the file.</param>
        /// <returns>True on success, false on error.</returns>
        protected bool CreateOrUpdate( IActivityMonitor m, string newContent, bool forceSave = false )
        {
            string oldContent = GetFile()?.TextContent;
            if( oldContent == newContent && !forceSave ) return true;
            if( newContent == null )
            {
                Delete( m );
                return true;
            }
            if( !FileSystem.CopyTo( m, newContent, FilePath ) )
            {
                return false;
            }
            _file = null;
            return true;
        }
    }
}
