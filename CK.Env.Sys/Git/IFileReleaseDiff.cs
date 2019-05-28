using CK.Env.Diff;

namespace CK.Env
{
    public interface IFileReleaseDiff
    {
        /// <summary>
        /// Gets the file path.
        /// </summary>
        string FilePath { get; }

        /// <summary>
        /// Gets the change type.
        /// </summary>
        FileReleaseDiffType DiffType { get; }
    }
}
