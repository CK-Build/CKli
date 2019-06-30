using System.Collections.Generic;

namespace CK.Env
{
    public interface IDiffResult
    {
        bool IsValid { get; }
        IReadOnlyList<IDiffRootResult> Diffs { get; }
        IDiffRootResult Others { get; }
    }
}
