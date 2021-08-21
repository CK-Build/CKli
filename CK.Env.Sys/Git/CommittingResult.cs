namespace CK.Env
{
    public enum CommittingResult : byte
    {
        NoChanges = 0,
        Commited = 1,
        Amended = 2,
        Error = byte.MaxValue
    }
}
