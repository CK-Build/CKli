namespace CKli.BranchModel.Plugin;

/// <summary>
/// Defines the type of link between a <see cref="BranchName"/> and its more stable parent.
/// </summary>
enum BranchLinkType
{
    /// <summary>
    /// No propagation at all ("||"). The branch are never automatically updated.
    /// </summary>
    None,

    /// <summary>
    /// Restricted propagation ("|"): only published stable releases are propagated downwards.
    /// </summary>
    Stable,

    /// <summary>
    /// This is the default link ("->"). Pre releases are propagated when published.
    /// </summary>
    PreRelease,

    /// <summary>
    /// Full link ("=>"). CI builds are propagated.
    /// </summary>
    Full
}
