using CK.Core;
using System;

namespace CKli.Core;

/// <summary>
/// A world's repository. Encapsulates a <see cref="GitRepository"/>.
/// </summary>
public sealed class Repo
{
    readonly World _world;
    // World.Dispose() disposes the Git repository.
    internal readonly GitRepository _git;
    readonly int _index;
    GitRepository.SimpleStatusInfo _status;

    internal Repo( World world, GitRepository git, int index )
    {
        _world = world;
        _git = git;
        _index = index;
    }

    /// <summary>
    /// Gets the World that contains this repository.
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
    /// Gets this short path to display for this repository.
    /// </summary>
    public NormalizedPath DisplayPath => _git.DisplayPath;

    /// <summary>
    /// Gets the git status.
    /// </summary>
    public GitRepository.SimpleStatusInfo GitStatus
    {
        get
        {
            if( _status.IsDefault )
            {
                _status = _git.GetSimpleStatusInfo();
            }
            return _status;
        }
    }

    /// <summary>
    /// Gets the index of this Repo in the World according to <see cref="WorldDefinitionFile.RepoOrder"/>.
    /// </summary>
    public int Index => _index;

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
    /// Fetches 'origin' (or all remotes) branches and tags into this repository.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="originOnly">False to fetch all the remote branches. By default, branches from only 'origin' remote are considered.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Fetch( IActivityMonitor monitor, bool originOnly = true ) => _git.FetchBranches( monitor, originOnly );

    /// <summary>
    /// Pushes changes from the current branch to the origin.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Push( IActivityMonitor monitor ) => _git.Push( monitor );

}
