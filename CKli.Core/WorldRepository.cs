using CK.Core;
using System;

namespace CKli.Core;


public sealed partial class WorldRepository : IDisposable
{
    readonly LocalWorldName _name;
    readonly GitRepository _git;

    WorldRepository( LocalWorldName name, GitRepository git )
    {
        _name = name;
        _git = git;
    }

    public LocalWorldName Name => _name;

    public void Dispose()
    {
        _git.Dispose();
    }
}
