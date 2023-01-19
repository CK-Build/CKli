
using CK.Core;

namespace CK.Env
{
    /// <summary>
    /// Models a deleted file in <see cref="GitDiffResult"/>.
    /// </summary>
    public interface IDeletedDiff
    {
        /// <summary>
        /// Path of the deleted file.
        /// </summary>
        NormalizedPath Path { get; }

    }
}
