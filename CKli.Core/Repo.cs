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
    internal readonly Repo? _nextRepo;
    GitRepository.SimpleStatusInfo _status;

    internal Repo( World world, GitRepository git, int index, Repo? nextRepo )
    {
        _world = world;
        _git = git;
        _index = index;
        _nextRepo = nextRepo;
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

    /// <inheritdoc cref="GitRepository.SetCurrentBranch(IActivityMonitor, string, bool)"/>
    public bool SetCurrentBranch( IActivityMonitor monitor, string branchName, bool skipPullMerge = false )
            => _git.SetCurrentBranch( monitor, branchName, skipPullMerge );

    /// <summary>
    /// Returns the <see cref="DisplayPath"/> (with its link to <see cref="WorkingFolder"/>) as a <see cref="ContentBox"/>
    /// or a <see cref="HorizontalContent"/> with it and:
    /// <list type="number">
    ///     <item>A box with its current branch name.</item>
    ///     <item>A Box with the commit remotes ↑0↓0 differences indicator.</item>
    ///     <item>A Box with the <see cref="Repo.OriginUrl"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="screenType">The screen type.</param>
    /// <param name="withBranchName">True to add a box with the current branch name.</param>
    /// <param name="withRemoteDiffCount">True to add a box with the commit remotes ↑0↓0 differences indicator.</param>
    /// <param name="withOriginUrl">True to add a box with the <see cref="OriginUrl"/>.</param>
    /// <returns>The renderable.</returns>
    public IRenderable ToRenderable( ScreenType screenType, bool withBranchName = false, bool withRemoteDiffCount = false, bool withOriginUrl = false )
    {
        var status = GitStatus;
        var folderStyle = new TextStyle( status.IsDirty ? ConsoleColor.DarkRed : ConsoleColor.DarkGreen, ConsoleColor.Black );

        IRenderable folder = screenType.Text( DisplayPath ).HyperLink( new Uri( $"file:///{WorkingFolder}" ) );
        if( status.IsDirty ) folder = folder.Box( paddingRight: 1 ).AddLeft( screenType.Text( "✱" ).Box( paddingRight: 1 ) );
        else folder = folder.Box( paddingLeft: 2, paddingRight: 1 );
        folder = folder.Box( style: folderStyle );

        if( withBranchName )
        {
            folder = folder.AddRight( screenType.Text( status.CurrentBranchName ).Box( marginRight: 1 ) );
        }
        if( withRemoteDiffCount )
        {
            if( status.IsTracked )
            {
                var diff = CommitDiff( screenType, '↑', status.CommitAhead.Value )
                           .AddRight( CommitDiff( screenType, '↓', status.CommitBehind.Value ) );
                folder = folder.AddRight( diff.Box( marginRight: 1 ) );
            }
            else
            {
                folder = folder.AddRight( screenType.Text( "<local>" ).Box( marginRight: 1 ) );
            }
        }
        if( withOriginUrl )
        {
            folder = folder.AddRight( screenType.Text( OriginUrl.ToString() ).HyperLink( OriginUrl ).Box( marginRight: 1 ) );
        }
        return folder;

        static IRenderable CommitDiff( ScreenType screenType, char aheadOrBehind, int count )
        {
            return screenType.Text( $"{aheadOrBehind}{count}",
                                    count != 0
                                        ? new TextStyle( new Color( System.ConsoleColor.Red, System.ConsoleColor.Black ), TextEffect.Bold )
                                        : TextStyle.None );
        }
    }

}
