namespace CKli.Core;

/// <summary>
/// Describes commit options.
/// </summary>
public enum CommitBehavior
{
    /// <summary>
    /// Always create a new commit.
    /// </summary>
    CreateNewCommit,

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
