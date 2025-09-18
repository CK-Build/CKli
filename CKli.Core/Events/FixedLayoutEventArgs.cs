using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

public sealed class FixedLayoutEventArgs : WorldEventArgs
{
    readonly IReadOnlyList<Repo> _newClones;

    public FixedLayoutEventArgs( IActivityMonitor monitor, World world, IReadOnlyList<Repo> newClones )
        : base( monitor, world )
    {
        _newClones = newClones;
    }

    public IReadOnlyList<Repo> NewClones => _newClones;
}
