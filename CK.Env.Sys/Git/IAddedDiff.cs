using CK.Text;

namespace CK.Env
{
    public interface IAddedDiff
    {
        /// <summary>
        /// Path of the created file.
        /// </summary>
        NormalizedPath Path { get; }
    }
}
