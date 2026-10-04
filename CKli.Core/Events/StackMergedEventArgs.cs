using CK.Core;
using System.Collections.Immutable;

namespace CKli.Core;

/// <summary>
/// Raised by <see cref="World.PullStackAsync(IActivityMonitor)"/> when the pull of the Stack created a merge commit.
/// <para>
/// The pull done when the Stack is opened happens before any plugin exists: its merge is exposed by
/// <see cref="StackRepository.MergedPaths"/> instead.
/// </para>
/// </summary>
public sealed class StackMergedEventArgs : WorldEventArgs
{
    readonly ImmutableArray<NormalizedPath> _mergedPaths;
    bool _success;

    internal StackMergedEventArgs( IActivityMonitor monitor,
                                   CKliEnv context,
                                   World world,
                                   ImmutableArray<NormalizedPath> mergedPaths )
        : base( monitor, context, world )
    {
        _mergedPaths = mergedPaths;
        _success = true;
    }

    /// <summary>
    /// Gets the paths, relative to the <see cref="StackRepository.StackWorkingFolder"/>, that the merge commit changed.
    /// Never empty.
    /// </summary>
    public ImmutableArray<NormalizedPath> MergedPaths => _mergedPaths;

    /// <summary>
    /// Gets whether no handler has called <see cref="SetFailed"/>.
    /// </summary>
    public bool Success => _success;

    /// <summary>
    /// Signals that a handler failed to handle the merge (the error must be logged): the pull fails.
    /// </summary>
    public void SetFailed() => _success = false;
}
