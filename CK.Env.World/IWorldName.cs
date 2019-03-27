namespace CK.Env
{
    /// <summary>
    /// Defines the "Long Term Support World". A World is identified by a name
    /// and a <see cref="LTSKey"/>.
    /// It exposes the 3 fundamentals branch names we handle:
    /// <see cref="MasterBranchName"/>, <see cref="DevelopBranchName"/> and <see cref="LocalBranchName"/>.
    /// </summary>
    public interface IWorldName
    {
        /// <summary>
        /// Gets tne name of this world.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the LTS key. Normalized to null for current.
        /// </summary>
        string LTSKey { get; }

        /// <summary>
        /// Gets the develop branch name.
        /// This branch is the primary one. It contains the default settings and configuration
        /// when no branch specific information exists.
        /// </summary>
        string DevelopBranchName { get; }

        /// <summary>
        /// Gets the master branch name.
        /// </summary>
        string MasterBranchName { get; }

        /// <summary>
        /// Gets the local branch name.
        /// </summary>
        string LocalBranchName { get; }

        /// <summary>
        /// Gets the <see cref="Name"/> or <see cref="Name"/>-<see cref="LTSKey"/> if the key is not null.
        /// </summary>
        string FullName { get; }
    }
}
