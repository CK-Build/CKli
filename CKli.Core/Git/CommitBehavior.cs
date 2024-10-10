namespace CKli.Core;

public enum CommitBehavior
{
    CreateNewCommit,
    AmendIfPossibleAndKeepPreviousMessage,
    AmendIfPossibleAndAppendPreviousMessage,
    AmendIfPossibleAndPrependPreviousMessage,
    AmendIfPossibleAndOverwritePreviousMessage
}
