using System;
using System.Collections.Generic;
using System.Text;

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
