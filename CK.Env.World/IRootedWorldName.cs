using CK.Text;

namespace CK.Env
{
    /// <summary>
    /// Extends <see cref="IWorldName"/> with a <see cref="Root"/> path.
    /// </summary>
    public interface IRootedWorldName : IWorldName
    {
        /// <summary>
        /// Gets the local world root directory path.
        /// This is <see cref="NormalizedPath.IsEmptyPath"/> if the world is not mapped (typically by the <see cref="IWorldLocalMapping"/>).
        /// </summary>
        NormalizedPath Root { get; }
    }
}
