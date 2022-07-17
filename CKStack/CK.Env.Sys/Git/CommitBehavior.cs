namespace CK.Env
{
    public enum CommitBehavior
    {
        CreateNewCommit,
        AmendIfPossibleAndKeepPreviousMessage,
        AmendIfPossibleAndAppendPreviousMessage,
        AmendIfPossibleAndPrependPreviousMessage,
        AmendIfPossibleAndOverwritePreviousMessage
    }
}
