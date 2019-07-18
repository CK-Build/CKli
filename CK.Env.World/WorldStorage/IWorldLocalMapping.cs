using CK.Text;

namespace CK.Env
{
    /// <summary>
    /// Enables mapping of a <see cref="IWorldName"/> to its local diretory.
    /// </summary>
    public interface IWorldLocalMapping
    {
        /// <summary>
        /// Get the local root directory path for a world.
        /// This root contains the world local state (by default), the "CKli-World.htm"
        /// file marker and the Git repositories.
        /// </summary>
        /// <param name="w">The world name.</param>
        /// <returns>The path to the root directory or <see cref="NormalizedPath.IsEmptyPath"/> if it is not mapped.</returns>
        NormalizedPath GetRootPath( IWorldName w );
    }
}
