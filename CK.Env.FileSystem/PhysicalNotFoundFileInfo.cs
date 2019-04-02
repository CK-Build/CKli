using CK.Text;
using Microsoft.Extensions.FileProviders;
using System;
using System.IO;

namespace CK.Env
{

    /// <summary>
    /// This is required since standard <see cref="NotFoundFileInfo"/> has a null <see cref="Path"/>.
    /// This is stupid since this is not because a file does not exist that it does not have a physical path...
    /// </summary>
    public class PhysicalNotFoundFileInfo : IFileInfo
    {
        public PhysicalNotFoundFileInfo( NormalizedPath fullPath )
        {
            FullPath = fullPath;
        }

        /// <summary>
        /// Gets the <see cref="PhysicalPath"/> as a <see cref="NormalizedPath"/>.
        /// </summary>
        public NormalizedPath FullPath { get; }

        /// <summary>
        /// Always false.
        /// </summary>
        public bool Exists => false;

        /// <summary>
        /// Always -1.
        /// </summary>
        public long Length => -1;

        /// <summary>
        /// Gets the path of this missing file.
        /// </summary>
        public string PhysicalPath => FullPath;

        /// <summary>
        /// Gets the file name (<see cref="NormalizedPath.LastPart"/>).
        /// </summary>
        public string Name => FullPath.LastPart;

        /// <summary>
        /// Always <see cref="DateTimeOffset.MinValue"/>.
        /// </summary>
        public DateTimeOffset LastModified => DateTimeOffset.MinValue;

        /// <summary>
        /// Always false.
        /// </summary>
        public bool IsDirectory => false;

        /// <summary>
        /// Always throw <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <returns></returns>
        public Stream CreateReadStream()
        {
            throw new InvalidOperationException( "Unexisting file can not be read." );
        }
    }
}
