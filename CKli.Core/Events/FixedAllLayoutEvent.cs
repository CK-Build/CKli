using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

public sealed class FixedAllLayoutEvent : WorldEvent
{
    readonly IReadOnlyList<Repo> _newClones;

    internal FixedAllLayoutEvent( IActivityMonitor monitor, World world, IReadOnlyList<Repo> newClones )
        : base( monitor, world )
    {
        _newClones = newClones;
    }

    public IReadOnlyList<Repo> NewClones => _newClones;
}
