using CK.Core;
using System.Collections.Generic;

namespace CKli.Core;

/// <summary>
/// Raised by the "ckli push" command once the repositories have been successfully pulled, before they are pushed.
/// <para>
/// "ckli push" pushes the local branches that track an "origin" branch. A handler calls <see cref="EnsureTracking"/>
/// to name the branches that must be pushed even when they don't track a remote branch (typically because they
/// have been created by a raw "git checkout -b"): such a local branch is configured to track its "origin" branch
/// and is pushed, creating the remote branch.
/// </para>
/// </summary>
public sealed class PushingEventArgs : WorldEventArgs
{
    readonly IReadOnlyList<Repo> _repos;
    HashSet<string>? _trackedBranchNames;
    bool _success;

    internal PushingEventArgs( IActivityMonitor monitor,
                               CKliEnv context,
                               World world,
                               IReadOnlyList<Repo> repos )
        : base( monitor, context, world )
    {
        _repos = repos;
        _success = true;
    }

    /// <summary>
    /// Gets the repositories that will be pushed.
    /// </summary>
    public IReadOnlyList<Repo> Repos => _repos;

    /// <summary>
    /// Declares a branch name that must track its "origin" branch: in each of the <see cref="Repos"/>, the local branch
    /// with this name that tracks no branch is configured to track its "origin" branch and is pushed.
    /// <para>
    /// A missing branch is ignored, a branch that already tracks a branch is left as-is and the "local/" and "building/"
    /// branches are never pushed.
    /// </para>
    /// </summary>
    /// <param name="branchName">The local branch name (like "dev/stable").</param>
    public void EnsureTracking( string branchName )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
        (_trackedBranchNames ??= new HashSet<string>()).Add( branchName );
    }

    internal IReadOnlySet<string>? TrackedBranchNames => _trackedBranchNames;

    /// <summary>
    /// Gets whether no handler has called <see cref="SetFailed"/>.
    /// </summary>
    public bool Success => _success;

    /// <summary>
    /// Signals that a handler failed (the error must be logged): nothing is pushed.
    /// </summary>
    public void SetFailed() => _success = false;
}
