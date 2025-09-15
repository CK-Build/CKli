using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// A world's repository. Encapsulates a <see cref="GitRepository"/>.
/// </summary>
public sealed class Repo
{
    readonly World _world;
    internal readonly GitRepository _git;

    public Repo( World world, GitRepository git )
    {
        _world = world;
        _git = git;
    }

    /// <summary>
    /// Gets the world that contains this repository.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets the 'origin' remote repository url.
    /// </summary>
    public Uri OriginUrl => _git.OriginUrl;

    /// <summary>
    /// Gets this repo's working folder.
    /// </summary>
    public NormalizedPath WorkingFolder => _git.WorkingFolder;

    /// <summary>
    /// Pull-Merge the current head from the remote using the default fast-forward strategy
    /// (see <see href="https://git-scm.com/docs/git-merge#Documentation/git-merge.txt-mergeff"/>).
    /// Any merge conflict is an error.
    /// <para>
    /// If the current head has no associated tracking branch, nothing is done. 
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>A MergeResult: both <see cref="MergeResult.Error"/> and <see cref="MergeResult.ErrorConflicts"/> are failures.</returns>
    public MergeResult Pull( IActivityMonitor monitor ) => _git.Pull( monitor,
                                                                      mergeFileFavor: LibGit2Sharp.MergeFileFavor.Normal,
                                                                      fastForwardStrategy: LibGit2Sharp.FastForwardStrategy.Default );

    /// <summary>
    /// 
    /// </summary>
    /// <param name="monitor"></param>
    /// <returns></returns>
    public bool Push( IActivityMonitor monitor ) => _git.Push( monitor );

}
