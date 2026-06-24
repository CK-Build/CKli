namespace CKli.BranchModel.Plugin;

/// <summary>
/// Defines the type of link between a <see cref="BranchName"/> and its more stable parent.
/// </summary>
public enum BranchLinkType
{
    /// <summary>
    /// No propagation at all ("||"): the "dev/" child branch must be manually updated.
    /// </summary>
    None,

    /// <summary>
    /// Restricted propagation ("|"): the "dev/" child branch is synchronized with the parent branch (a stable or a prerelease
    /// must be built on the parent branch to impact the child).
    /// <para>
    /// Commits of stable or prerelease versions are merged into the "dev/" child branch. 
    /// </para>
    /// </summary>
    Release,

    /// <summary>
    /// This is the default link ("->"): the "dev/" child branch is synchronized with the "dev/" parent branch but only on built commits
    /// (a build or build --ci must be done on the parent branch to impact the child).
    /// <para>
    /// All versioned commits are merged into the "dev/" child branch. 
    /// </para>
    /// </summary>
    CI,

    /// <summary>
    /// Full link ("=>"): the "dev/" child branch is synchronized with the "dev/" parent branch.
    /// </summary>
    Full
}
