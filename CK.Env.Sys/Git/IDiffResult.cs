using System.Collections.Generic;

namespace CK.Env
{
    public interface IDiffResult
    {
        IReadOnlyList<IDiffRootResult> Diffs { get; }

        IDiffRootResult Others { get; }
    }
}
