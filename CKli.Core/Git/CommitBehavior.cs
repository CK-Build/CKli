namespace CKli.Core;

/// <summary>
/// Describes commit options.
/// </summary>
public enum CommitBehavior
{
    /// <summary>
    /// Don't try to amend the current commit.
    /// Creates a new commit if there's something to commit or does nothing if there's nothing to commit.
    /// </summary>
    CreateNewCommit,

    /// <summary>
    /// Don't try to amend the current commit and always create a new commit even if there's something to commit.
    /// </summary>
    CreateEmptyCommit,

    /// <summary>
    /// Amends the commit if possible, keeping the previous message.
    /// </summary>
    AmendIfPossibleAndKeepPreviousMessage,

    /// <summary>
    /// Amends the commit if possible, appending the previous message after the current one.
    /// </summary>
    AmendIfPossibleAndAppendPreviousMessage,

    /// <summary>
    /// Amends the commit if possible, appending the current message after the existing ones.
    /// </summary>
    AmendIfPossibleAndPrependPreviousMessage,

    /// <summary>
    /// Amends the commit if possible, replacing any previous message.
    /// </summary>
    AmendIfPossibleAndOverwritePreviousMessage
}
