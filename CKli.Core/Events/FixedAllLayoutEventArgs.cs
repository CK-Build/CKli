using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

public sealed class FixedAllLayoutEventArgs : WorldEventArgs
{
    readonly IReadOnlyList<Repo> _newClones;

    public FixedAllLayoutEventArgs( IActivityMonitor monitor, World world, IReadOnlyList<Repo> newClones )
        : base( monitor, world )
    {
        _newClones = newClones;
    }

    public IReadOnlyList<Repo> NewClones => _newClones;
}
