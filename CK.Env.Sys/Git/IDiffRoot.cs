using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public interface IDiffRoot
    {
        string Name { get; }
        IEnumerable<NormalizedPath> Paths { get;  }
    }
}
