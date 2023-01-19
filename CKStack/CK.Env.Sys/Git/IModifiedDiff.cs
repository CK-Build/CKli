using CK.Core;

namespace CK.Env
{
    /// <summary>
    /// Models a modified file in <see cref="GitDiffResult"/>.
    /// </summary>
    public interface IModifiedDiff
    {
        /// <summary>
        /// Old path of the modified file.
        /// </summary>
        NormalizedPath OldPath { get; }

        /// <summary>
        /// New path of the modified file.
        /// </summary>
        NormalizedPath NewPath { get; }

    }
}
