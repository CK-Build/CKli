using CK.Core;
using System.Collections.Generic;

namespace CK.Env.Diff
{
    public class DiffRoot : IDiffRoot
    {
        public DiffRoot( string name, IEnumerable<NormalizedPath> paths )
        {
            Name = name;
            Paths = paths;
        }

        public string Name { get; }

        public IEnumerable<NormalizedPath> Paths { get; }
    }
}
