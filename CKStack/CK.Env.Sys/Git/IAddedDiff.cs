using CK.Core;

namespace CK.Env
{
    /// <summary>
    /// Models a new file in <see cref="GitDiffResult"/>.
    /// </summary>
    public interface IAddedDiff
    {
        /// <summary>
        /// Path of the created file.
        /// </summary>
        NormalizedPath Path { get; }
    }
}
