using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Event raised by "ckli layout fix" when the world's layout has been fixed.
/// </summary>
public sealed class FixedAllLayoutEventArgs : WorldEventArgs
{
    readonly IReadOnlyList<Repo> _newClones;

    internal FixedAllLayoutEventArgs( IActivityMonitor monitor, CKliEnv context, World world, IReadOnlyList<Repo> newClones )
        : base( monitor, context, world )
    {
        _newClones = newClones;
    }

    /// <summary>
    /// Gets a list of new repositories.
    /// </summary>
    public IReadOnlyList<Repo> NewClones => _newClones;
}
