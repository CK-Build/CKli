using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Event raised by "ckli layout fix" when the world's layout has been fixed.
/// </summary>
public sealed class FixedAllLayoutEvent : WorldEvent
{
    readonly IReadOnlyList<Repo> _newClones;

    internal FixedAllLayoutEvent( IActivityMonitor monitor, World world, IReadOnlyList<Repo> newClones )
        : base( monitor, world )
    {
        _newClones = newClones;
    }

    /// <summary>
    /// Gets a list of new repositories.
    /// </summary>
    public IReadOnlyList<Repo> NewClones => _newClones;
}
