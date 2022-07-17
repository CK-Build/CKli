using CK.Core;

using System;
using System.Text;

#nullable enable

namespace CK.Env
{
    /// <summary>
    /// Basic cache of a text file content with helpers to manage it (update, create or delete).
    /// </summary>
    public abstract class TextFileBase
    {
        ITextFileInfo? _file;
        readonly Encoding _encoding;

        /// <summary>
        /// Gets the UTF-8 encoding with BOM: the <see cref="Encoding.UTF8"/>
        /// rather than the <see cref="Encoding.Default"/> that is (on NetCore)
        /// UTF8 without BOM.
        /// </summary>
        public readonly static Encoding UTF8EncodingWithBOM = Encoding.UTF8;

        /// <summary>
        /// Initializes a new <see cref="TextFileBase"/>.
        /// You can use <see cref="UTF8EncodingWithBOM"/> to specify that BOM should be emitted.
        /// </summary>
        /// <param name="fs">The file system.</param>
        /// <param name="filePath">The path to the file relative to the file system root.</param>
        /// <param name="encoding">The encoding that defaults to UTF-8 (without Byte Order Mask).</param>
        protected TextFileBase( FileSystem fs, NormalizedPath filePath, Encoding? encoding = null )
        {
            FileSystem = fs;
            FilePath = filePath;
            _encoding = encoding ?? Encoding.Default;
        }

        /// <summary>
        /// Fires whenever this file has been deleted or saved.
        /// </summary>
        public event EventHandler<EventMonitoredArgs>? OnSavedOrDeleted;

        ITextFileInfo? GetFile() => _file ?? (_file = FileSystem.GetFileInfo( FilePath ).AsTextFileInfo( ignoreExtension: true ));

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
        public string? TextContent => GetFile()?.TextContent;

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
                OnDeleted( m );
            }
        }

        /// <summary>
        /// Called by <see cref="Delete"/> once <see cref="TextContent"/> is null
        /// and the file has been deleted from the file system.
        /// Raises the <see cref="OnSavedOrDeleted"/> event.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        protected virtual void OnDeleted( IActivityMonitor m )
        {
            OnSavedOrDeleted?.Invoke( this, new EventMonitoredArgs( m ) );
        }

        /// <summary>
        /// Called by <see cref="CreateOrUpdate"/> once the text file
        /// has been updated (the file necessarily exists).
        /// Raises the <see cref="OnSavedOrDeleted"/> event.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        protected virtual void OnSaved( IActivityMonitor m )
        {
            OnSavedOrDeleted?.Invoke( this, new EventMonitoredArgs( m ) );
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
        protected bool CreateOrUpdate( IActivityMonitor m, string? newContent, bool forceSave = false )
        {
            string? oldContent = GetFile()?.TextContent;
            if( oldContent == newContent && !forceSave ) return true;
            if( newContent == null )
            {
                Delete( m );
                return true;
            }
            if( !FileSystem.CopyTo( m, newContent, FilePath, _encoding ) )
            {
                return false;
            }
            OnSaved( m );
            _file = null;
            return true;
        }
    }
}
