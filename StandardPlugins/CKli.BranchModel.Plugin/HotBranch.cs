using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Models a branch defined by a <see cref="BranchName"/> in a <see cref="BranchModelInfo"/> for a repository.
/// <para>
/// The actual <see cref="GitBranch"/> and/or <see cref="GitDevBranch"/> may not exist.
/// </para>
/// </summary>
public sealed class HotBranch
{
    readonly BranchName _name;
    readonly BranchModelInfo _info;
    BranchLink? _link;
    Branch? _gitDevBranch;

    HotBranch( BranchName name, BranchModelInfo info, BranchLink? link, Branch? gitDevBranch )
    {
        _name = name;
        _info = info;
        _link = link;
        _gitDevBranch = gitDevBranch;
    }

    internal static HotBranch Create( IActivityMonitor monitor, BranchModelInfo info, GitRepository repo, BranchName name )
    {
        DoCreate( monitor, repo, name, out BranchLink? link, out Branch? gitDevBranch );
        return new HotBranch( name, info, link, gitDevBranch );
    }

    static void DoCreate( IActivityMonitor monitor, GitRepository repo, BranchName name, out BranchLink? link, out Branch? gitDevBranch )
    {
        var gitBranch = repo.GetBranch( monitor, name.Name, missingLocalAndRemote: CK.Core.LogLevel.None );
        gitDevBranch = repo.GetBranch( monitor, name.DevName, missingLocalAndRemote: CK.Core.LogLevel.None );
        if( gitBranch != null )
        {
            link = gitDevBranch == null
                    ? BranchLink.Create( gitBranch, name.DevName )
                    : BranchLink.Create( gitBranch, gitDevBranch );
        }
        else
        {
            link = null;
        }
    }

    /// <summary>
    /// Refreshes this branch state and returns <see cref="Exists"/>.
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <returns>Whether the <see cref="GitBranch"/> exists in the repository (ie. <see cref="Exists"/> is true).</returns>
    [MemberNotNullWhen( true, nameof( GitBranch ), nameof( _link ) )]
    public bool Refresh( IActivityMonitor monitor )
    {
        if( _link != null )
        {
            _link = _link.Refresh( monitor, Repo.GitRepository );
            _gitDevBranch = _link?.Ahead;
        }
        else
        {
            DoCreate( monitor, Repo.GitRepository, _name, out _link, out _gitDevBranch );
        }
        return Exists;
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _info.Repo;

    /// <summary>
    /// Gets the branch information.
    /// </summary>
    public BranchModelInfo BranchModelInfo => _info;

    /// <summary>
    /// Gets the branch name from the branch namespace.
    /// </summary>
    public BranchName BranchName => _name;

    /// <summary>
    /// Gets the repository's branch if it exists.
    /// </summary>
    public Branch? GitBranch => _link?.Branch;

    /// <summary>
    /// Gets the repository's "dev/<see cref="GitBranch"/>" branch if it exists.
    /// </summary>
    public Branch? GitDevBranch => _gitDevBranch;

    /// <summary>
    /// Gets whether the <see cref="GitBranch"/> exists in this <see cref="Repo"/>.
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitBranch ), nameof( _link ) )]
    public bool Exists => _link != null;

    /// <summary>
    /// Gets whether <see cref="GitDevBranch"/> exists but this branch is not active.
    /// <para>
    /// The <see cref="GitDevBranch"/> branch should be deleted.
    /// </para>
    /// </summary>
    [MemberNotNullWhen( true, nameof( GitDevBranch ) )]
    public bool HasOrphanDevBranch => _link == null && _gitDevBranch != null;

    /// <summary>
    /// Gets whether this branch has an issue that should be collected and fixed.
    /// <para>
    /// This is false when the <see cref="GitDevBranch"/> branch is <see cref="BranchLink.IssueKind.Useless"/>: this is a minor issue
    /// that is collected by "ckli issue" but is not treated as a real issue. When <paramref name="deleteUselessDevBranch"/> is true,
    /// the useless "dev/" branch is silently deleted.
    /// </para>
    /// </summary>
    /// <returns>True if this branch has issue (other than being useless), false otherwise.</returns>
    public bool HasIssue( IActivityMonitor monitor, bool deleteUselessDevBranch ) => HasIssue( deleteUselessDevBranch ? monitor : null );

    /// <summary>
    /// Gets whether this branch has an issue that should be collected and fixed.
    /// <para>
    /// See <see cref="HasIssue(IActivityMonitor, bool)"/>.
    /// </para>
    /// </summary>
    /// <returns>True if this branch has issue (other than being useless), false otherwise.</returns>
    public bool HasIssue() => HasIssue( null );

    bool HasIssue( IActivityMonitor? monitor )
    {
        if( _link == null )
        {
            return _name.Index == 0 || _gitDevBranch != null;
        }
        var i = _link.Issue;
        if( i == BranchLink.IssueKind.Useless )
        {
            if( monitor != null )
            {
                Throw.DebugAssert( _gitDevBranch != null );
                if( _gitDevBranch.IsCurrentRepositoryHead )
                {
                    Throw.DebugAssert( """
                                Issue is NOT Useless if branch is checked out and the repo is dirty:
                                we can use force: true to skip the CheckCleanCommit call.
                                """,
                                !Repo.GitRepository.GetSimpleStatusInfo().IsDirty );
                    // On error, we throw here: this has no reason to fail and continuing could be really bad.
                    if( !Repo.GitRepository.Checkout( monitor, _link.Branch, force: true ) )
                    {
                        throw new CKException( $"Unable to check out '{_name.Name}' in '{Repo.DisplayPath}' to delete useless '{_name.DevName}' branch." );
                    }
                }
                Repo.GitRepository.Repository.Branches.Remove( _gitDevBranch );
                _link = BranchLink.Create( _link.Branch, _name.DevName );
                _gitDevBranch = null;
            }
            return false;
        }
        return i != BranchLink.IssueKind.None;
    }

    internal void Collect( BranchIssueBuilder issues )
    {
        if( _link == null )
        {
            // When _name.Index == 0, it is the "Missing root branch" case.
            // We don't handle it here but we avoid (if the "dev/stable" branch
            // exist) to enlist the "orphan dev/" issue: the existing "dev/stable" is used
            // as the starting point (instead of "main" or "master").
            if( _name.Index != 0 && _gitDevBranch != null )
            {
                issues.OnMissingBaseBranch( _gitDevBranch, _name.Name );
            }
        }
        else
        {
            _link.CollectIssue( issues );
        }
    }

    /// <summary>
    /// Gets the parent <see cref="HotBranch"/> in <see cref="BranchModelInfo.Branches"/>.
    /// Null if this is the root branch.
    /// </summary>
    public HotBranch? Parent => _name.Parent != null ? _info.Branches[_name.Parent.Index] : null;


    /// <summary>
    /// Ensure that the <see cref="GitBranch"/> exists: creating it from the closest existing branch
    /// if needed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool EnsureExists( IActivityMonitor monitor )
    {
        if( _link != null ) return true;
        var closest = _info.GetRequiredClosestExistingBranch( monitor, _name );
        if( closest == null ) return false;
        Throw.DebugAssert( closest.Exists );
        // The branch provided by the user doesn't exist. We create the hot branch
        // from the closest one.
        Throw.DebugAssert( """
                    Nothing could have fetched or create the branch since the HotBranch has been created
                    (GetBranch has been called - an existing remote would have created the local).
                    """, Repo.GitRepository.GetBranch( monitor, _name.Name, CK.Core.LogLevel.None ) == null );
        // Since the branch doesn't exist, we create it from its closest active branch regardless
        // of any configured link type (the branch must start somewhere).
        var gitBranch = Repo.GitRepository.EnsureIntegratedBranch( monitor, _name.Name, closest.GitBranch.Tip );
        return gitBranch != null && Refresh( monitor );
    }


    /// <summary>
    /// Ensures that this <see cref="GitBranch"/> and <see cref="GitDevBranch"/> are synchronized with their
    /// remote origin counterparts (if any) and with the <see cref="BranchModelInfo.GetClosestExistingBranch(BranchName)"/>
    /// according to <see cref="BranchName.LinkType"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="commitProvider">
    /// Required commit information provider to support <see cref="BranchLinkType.Release"/> and <see cref="BranchLinkType.CI"/>.
    /// </param>
    /// <param name="applyLink">Optional link type to consider. By default, this configured <see cref="BranchName.LinkType"/> is considered.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Synchronize( IActivityMonitor monitor, ITagCommitProvider? commitProvider, BranchLinkType applyLink = BranchLinkType.None )
    {
        Throw.CheckState( Exists );
        // Merge the tracked branches of the regular and the dev/ if they exist.
        var l = _link.MergeTrackedBranches( monitor, Repo.GitRepository, out var mergeTrackedError );
        if( l == null )
        {
            if( !mergeTrackedError )
            {
                monitor.Error( $"Branch '{_name}' in '{Repo.DisplayPath}' no more exists. Unable to synchronize it." );
            }
            return false;
        }
        // Merge the regular in the dev/ if needed.
        l = l.SynchronizeAhead( monitor, Repo.GitRepository );
        if( l == null )
        {
            return false;
        }
        // None => this configured link type.
        if( applyLink == BranchLinkType.None )
        {
            applyLink = _name.LinkType;
        }
        // If type is independent from parent branches, we're done.
        if( applyLink is BranchLinkType.None or BranchLinkType.Manual )
        {
            _link = l;
            _gitDevBranch = l.Ahead;
            return true;
        }
        // We get the closest existing branch, synchronize it (origin remotes only).
        Throw.DebugAssert( _name.Parent != null );
        var parent = _info.GetRequiredClosestExistingBranch( monitor, _name.Parent );
        if( parent == null )
        {
            return false;
        }
        Throw.DebugAssert( parent.Exists );
        if( !parent.Synchronize( monitor, commitProvider, BranchLinkType.None ) )
        {
            return false;
        }
        // We always target the "dev/" branch if a merge must be done but the "dev/" may not exist.
        var thisBranch = _gitDevBranch ?? GitBranch;

        // Full: Make sure that the closest existing branch "dev/" (or base) branch is integrated into this branch.
        //       If not, the "dev/" (or base) branch is merged into this "dev/" branch.
        if( applyLink is BranchLinkType.Full )
        {
            var parentBranch = parent.GitDevBranch ?? parent.GitBranch;
            var d = Repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( parentBranch.Tip, thisBranch.Tip );
            if( d.AheadBy is not 0 )
            {
                var dev = EnsureDevBranch();
                if( !Repo.GitRepository.MergeBranch( monitor, ref dev, parentBranch )
                    || !Refresh( monitor ) )
                {
                    return false;
                }
            }
            return true;
        }
        Throw.DebugAssert( applyLink is BranchLinkType.Release or BranchLinkType.CI );
        // We must find the commit and make sure that it is integrated in this branch.
        Throw.DebugAssert( _name.Parent != null );

        Throw.CheckNotNullArgument( "Required for BranchLinkType Release or CI.", commitProvider );

        var tagCommit = commitProvider.GetCommit( monitor, parent, applyLink is BranchLinkType.CI );
        if( tagCommit == null )
        {
            return false;
        }
        var cd = Repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( tagCommit.Commit, thisBranch.Tip );
        if( cd.AheadBy is not 0 )
        {
            var dev = EnsureDevBranch();
            if( !Repo.GitRepository.MergeBranch( monitor, ref dev, tagCommit.Commit )
                || !Refresh( monitor ) )
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Commits into this <see cref="BranchLink"/>. Development always takes place in the "dev/" branch,
    /// only <see cref="IntegrateDevBranch(IActivityMonitor)"/> can commit in the <see cref="GitBranch"/>.
    /// <list type="bullet">
    ///     <item>The <see cref="GitDevBranch"/> must exists and be checked out.</item>
    ///     <item>If there's nothing to commit, nothing is done (<paramref name="message"/> is ignored).</item>
    ///     <item>On success, GitDevBranch is updated.</item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Commit( IActivityMonitor monitor, string message )
    {
        Throw.CheckState( Exists && GitDevBranch != null && GitDevBranch.IsCurrentRepositoryHead );
        var newLink = _link.CommitAhead( monitor, Repo.GitRepository, message );
        if( newLink == null ) return false;
        _link = newLink;
        _gitDevBranch = _link.Ahead;
        return true;
    }

    /// <summary>
    /// Integrates <see cref="GitDevBranch"/> (that must not be null) into <see cref="GitBranch"/> and deletes it.
    /// On success, GitBranch is updated and GitDevBranch becomes null.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool IntegrateDevBranch( IActivityMonitor monitor )
    {
        Throw.CheckState( Exists && GitDevBranch != null );
        var newLink = _link.IntegrateAhead( monitor, Repo.GitRepository );
        if( newLink == null ) return false;
        _link = newLink;
        Throw.DebugAssert( _link.Ahead == null );
        _gitDevBranch = null;
        return true;
    }

    /// <summary>
    /// Ensures that <see cref="GitDevBranch"/> exists.
    /// </summary>
    /// <param name="withEmptyInitializationCommit">True to create new empty initialization commit if the ahead branch is missing.</param>
    /// <returns>The "dev/" branch.</returns>
    [MemberNotNull( nameof( GitDevBranch ) )]
    public Branch EnsureDevBranch( bool withEmptyInitializationCommit = false )
    {
        Throw.CheckState( Exists );
        if( _gitDevBranch == null )
        {
            Throw.DebugAssert( _link.Ahead == null );
            _link = _link.EnsureAhead( Repo.GitRepository, withEmptyInitializationCommit );
            _gitDevBranch = _link.Ahead;
        }
        Throw.DebugAssert( GitDevBranch != null );
        return _gitDevBranch!;
    }

    /// <summary>
    /// Ensures that if <see cref="GitDevBranch"/> exists, the base <see cref="Branch"/> has no commits that are not
    /// in the "dev/" branch.
    /// </summary>
    /// <returns>True on success, false on error.</returns>
    public bool SynchronizeDevBranch( IActivityMonitor monitor )
    {
        Throw.CheckState( Exists );
        var newLink = _link.SynchronizeAhead( monitor, Repo.GitRepository );
        if( newLink == null ) return false;
        _link = newLink;
        return true;
    }

    /// <summary>
    /// Returns this branch name.
    /// </summary>
    /// <returns>The name of this branch.</returns>
    public override string ToString() => _name.ToString();
}
