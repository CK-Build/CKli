namespace CK.Env
{
    /// <summary>
    /// Enables mapping of a <see cref="IWorldName"/> to its local diretory.
    /// </summary>
    public interface ILocalWorldRootPathMapping
    {
        /// <summary>
        /// Get the local root directory path for a world.
        /// This root contains the world state, the "CKli-World.htm" file marker and
        /// the Git repositories.
        /// </summary>
        /// <param name="w">The world name.</param>
        /// <returns>The path to the root directory or null if it is not mapped.</returns>
        string GetRootPath( IWorldName w );
    }
}
