namespace CK.Env.Diff
{
    public class FileReleaseDiff : IFileReleaseDiff
    {
        internal FileReleaseDiff( string path, FileReleaseDiffType c )
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
