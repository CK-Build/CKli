using CK.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Env
{
    public interface IAngularWorkspaceSpec
    {
        NormalizedPath Path { get; }
    }
}
