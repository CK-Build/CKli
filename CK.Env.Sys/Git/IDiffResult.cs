using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Env
{
    public interface IDiffResult
    {
        bool IsValid { get; }
        IReadOnlyList<IDiffRootResult> Diffs { get; }
        IDiffRootResult Others { get; }
    }
}
