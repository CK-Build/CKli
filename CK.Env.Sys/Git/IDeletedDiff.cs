using CK.Text;

namespace CK.Env
{
    public interface IDeletedDiff
    {
        /// <summary>
        /// Path of the deleted file.
        /// </summary>
        NormalizedPath Path { get; }

    }
}
