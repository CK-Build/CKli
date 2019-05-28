using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env.Diff
{
    public class DiffRoot : IDiffRoot
    {
        public DiffRoot(string name, IEnumerable<NormalizedPath> paths)
        {
            Name = name;
            Paths = paths;
        }

        public string Name { get; }

        public IEnumerable<NormalizedPath> Paths { get;  }
    }
}
