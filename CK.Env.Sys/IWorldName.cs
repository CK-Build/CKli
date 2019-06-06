namespace CK.Env
{
    /// <summary>
    /// Defines a "World" that is identified by a name and an optional <see cref="ParallelName"/>.
    /// It exposes the 3 fundamentals branch names we handle:
    /// <see cref="MasterBranchName"/>, <see cref="DevelopBranchName"/> and <see cref="LocalBranchName"/>.
    /// </summary>
    public interface IWorldName
    {
        /// <summary>
        /// Gets the base name of this world: this is the name of the "Stack".
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the parallel name. Normalized to null for default world.
        /// </summary>
        string ParallelName { get; }

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
        /// Gets the <see cref="Name"/> or <see cref="Name"/>[<see cref="ParallelName"/>] if the ParallelName is not null.
        /// </summary>
        string FullName { get; }
    }
}
