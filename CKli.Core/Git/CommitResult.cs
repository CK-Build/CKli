using CK.Core;

namespace CKli.Core;

/// <summary>
/// Result of a <see cref="GitRepository.Commit(IActivityMonitor, string, CommitBehavior)"/>.
/// </summary>
public enum CommitResult 
{
    /// <summary>
    /// An error occurred.
    /// </summary>
    Error,

        /// <summary>
    /// No change is the working folder.
    /// </summary>
    NoChanges,

    /// <summary>
    /// A commit has been created.
    /// </summary>
    Commited,

    /// <summary>
    /// THe current head has been amended.
    /// </summary>
    Amended,
}
