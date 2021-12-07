using CK.Core;
using System.Collections.Generic;

namespace CK.Env
{
    public interface IDiffRoot
    {
        string Name { get; }

        IEnumerable<NormalizedPath> Paths { get; }
    }
}
