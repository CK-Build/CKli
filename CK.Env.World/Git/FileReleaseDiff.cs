using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public class FileReleaseDiff
    {
        public FileReleaseDiff( string path, FileReleaseDiffType c )
        {
            FilePath = path;
            DiffType = c;
        }

        /// <summary>
        /// Gets the file path.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Gets the change type.
        /// </summary>
        public FileReleaseDiffType DiffType { get; }


    }
}
