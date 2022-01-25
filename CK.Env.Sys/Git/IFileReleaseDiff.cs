
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
        FileChangeKind DiffType { get; }
    }
}
