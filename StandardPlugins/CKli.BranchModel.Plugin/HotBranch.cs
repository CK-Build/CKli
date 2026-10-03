using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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
    /// that is collected by "ckli issue" but is not treated as a real issue.
    /// </para>
    /// <para>
    /// When <paramref name="autoFixDevBranch"/> is true, the "dev/" branch is made consistent with its base: a useless "dev/"
    /// branch is silently deleted and the missing base of an orphan "dev/" branch (<see cref="HasOrphanDevBranch"/>) is
    /// recreated (see <see cref="RestoreMissingBase"/>).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="autoFixDevBranch">True to fix the "dev/" branch.</param>
    /// <returns>True if this branch has issue (other than being useless), false otherwise.</returns>
    public bool HasIssue( IActivityMonitor monitor, bool autoFixDevBranch ) => HasIssue( autoFixDevBranch ? monitor : null );

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
            if( _name.Index == 0 ) return true;
            if( _gitDevBranch == null ) return false;
            // An orphan "dev/" branch: once its base is recreated, the restored pair is evaluated (the "dev/"
            // branch may well be useless).
            return monitor == null || !RestoreMissingBase( monitor ) || HasIssue( monitor );
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

    /// <summary>
    /// Recreates the missing base of an orphan "dev/" branch (<see cref="HasOrphanDevBranch"/>) where the "dev/" branch
    /// left its closest existing parent: the merge base of the "dev/" tip and the parent's tip ("dev/" first). Nothing is
    /// lost: the "dev/" branch keeps its commits, ahead of its recreated base.
    /// <para>
    /// A "dev/" branch implies its base, but a base can be missing: a "dev/" branch can reach the remote without it and
    /// be fetched by another clone, or the base can be deleted locally.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false when the root branch is missing or when the "dev/" branch has no common ancestor with its parent.</returns>
    internal bool RestoreMissingBase( IActivityMonitor monitor )
    {
        Throw.DebugAssert( HasOrphanDevBranch && _name.Parent != null );
        var dev = _gitDevBranch!;
        var parent = _info.GetClosestExistingBranch( _name.Parent );
        if( parent == null ) return false;
        var parentTip = (parent.GitDevBranch ?? parent.GitBranch!).Tip;
        var git = Repo.GitRepository.Repository;
        var forkPoint = git.ObjectDatabase.FindMergeBase( dev.Tip, parentTip );
        if( forkPoint == null )
        {
            monitor.Warn( $"""
                Branch '{dev.FriendlyName}' in '{Repo.DisplayPath}' has no common ancestor with '{parent.BranchName}':
                its missing base '{_name.Name}' cannot be recreated.
                """ );
            return false;
        }
        var b = git.Branches.Add( _name.Name, forkPoint );
        monitor.Info( ScreenType.CKliScreenTag,
                      $"Recreated the missing branch '{_name.Name}' of '{dev.FriendlyName}' in '{Repo.DisplayPath}' where it left '{parent.BranchName}'." );
        _link = BranchLink.Create( b, dev );
        return true;
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
                issues.OnMissingBaseBranch( this );
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
    public HotBranch? Parent => _name.IsRoot ? null : _info.Branches[_name.Parent.Index];


    /// <summary>
    /// Gets the commit that this non existing branch would be created at: the <see cref="BranchName.LinkType"/>
    /// decides it, because creation is the only moment it can be honored.
    /// <list type="bullet">
    ///     <item>
    ///     <see cref="BranchLinkType.None"/> and <see cref="BranchLinkType.Manual"/> propagate nothing from
    ///     the parent: the closest existing branch's tip.
    ///     </item>
    ///     <item>
    ///     <see cref="BranchLinkType.Full"/>: the closest existing branch's "dev/" tip when it exists (its
    ///     regular tip otherwise) - this is what <see cref="Synchronize"/> would have merged.
    ///     </item>
    ///     <item>
    ///     <see cref="BranchLinkType.Release"/> and <see cref="BranchLinkType.CI"/>: the last built commit of
    ///     the closest existing branch (CI builds are considered only for the CI link), obtained from the
    ///     <see cref="BranchModelPlugin.SetTagCommitProvider(ITagCommitProvider)"/>. When no provider has been
    ///     set (a World without the VersionTag plugin) this falls back on the closest existing branch's tip;
    ///     when the provider fails (unhealthy version tags) this fails.
    ///     </item>
    /// </list>
    /// <para>
    /// <see cref="Exists"/> must be false. This is what <see cref="EnsureExists(IActivityMonitor)"/> creates
    /// the branch at: whoever needs to know the content this branch will start with must read this commit.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The commit to start from or null on error.</returns>
    public Commit? GetStartCommit( IActivityMonitor monitor )
    {
        Throw.CheckState( !Exists );
        var closest = _info.GetRequiredClosestExistingBranch( monitor, _name );
        if( closest == null ) return null;
        Throw.DebugAssert( closest.Exists );
        var linkType = _name.LinkType;
        // No propagation from the parent at all: the branch must start somewhere and that is the
        // closest active branch. This is also the root branch's link type, but a missing root branch
        // has already been rejected by GetRequiredClosestExistingBranch.
        if( linkType is BranchLinkType.None or BranchLinkType.Manual )
        {
            return closest.GitBranch.Tip;
        }
        if( linkType is not BranchLinkType.Full && _info._plugin.TagCommitProvider == null )
        {
            // A World can enable the BranchModel without the VersionTag plugin: there is no notion of a
            // "last built commit" here. The floor keeps the branch creatable (Synchronize would throw).
            monitor.Warn( $"""
                    No ITagCommitProvider available: unable to honor the '{linkType}' link type of branch '{_name}'
                    in '{Repo.DisplayPath}'. Using the '{closest.BranchName}' branch tip.
                    """ );
            return closest.GitBranch.Tip;
        }
        // Starting at the link commit is what Synchronize would have obtained by merging it, without the
        // merge commit.
        var linkCommit = GetLinkCommit( monitor, closest, linkType );
        if( linkCommit == null )
        {
            monitor.Error( $"""
                    Unable to find the last {(linkType is BranchLinkType.CI ? "built" : "released")} commit of branch
                    '{closest.BranchName}' in '{Repo.DisplayPath}': the '{linkType}' link type of branch '{_name}'
                    cannot be honored.
                    """ );
        }
        return linkCommit;
    }

    /// <summary>
    /// Gets the commit of <paramref name="parent"/> that the <paramref name="linkType"/> propagates to this branch:
    /// <list type="bullet">
    ///     <item>
    ///     <see cref="BranchLinkType.Full"/>: the parent's "dev/" tip when it exists, its regular tip otherwise.
    ///     </item>
    ///     <item>
    ///     <see cref="BranchLinkType.Release"/> and <see cref="BranchLinkType.CI"/>: the last built commit of the
    ///     parent (CI builds are considered only for the CI link), obtained from the
    ///     <see cref="BranchModelPlugin.SetTagCommitProvider(ITagCommitProvider)"/> that must have been set.
    ///     </item>
    /// </list>
    /// <see cref="BranchLinkType.None"/> and <see cref="BranchLinkType.Manual"/> propagate nothing: they are not
    /// valid here.
    /// <para>
    /// This reads the current state of the repository: nothing is fetched nor merged, and the parent is not
    /// synchronized with its remote first (<see cref="Synchronize"/> does it before calling this).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="parent">
    /// The closest existing parent of this branch (see <see cref="BranchModelInfo.GetClosestExistingBranch(BranchName)"/>).
    /// </param>
    /// <param name="linkType">The link type to consider (this <see cref="BranchName.LinkType"/> or an override).</param>
    /// <returns>The commit to integrate or null if the tag commit provider failed.</returns>
    public Commit? GetLinkCommit( IActivityMonitor monitor, HotBranch parent, BranchLinkType linkType )
    {
        Throw.CheckArgument( parent.Exists && parent.Repo == Repo );
        Throw.CheckArgument( linkType is BranchLinkType.Release or BranchLinkType.CI or BranchLinkType.Full );
        if( linkType is BranchLinkType.Full )
        {
            return (parent.GitDevBranch ?? parent.GitBranch).Tip;
        }
        var commitProvider = _info._plugin.TagCommitProvider;
        Throw.CheckState( "Required for BranchLinkType Release or CI.", commitProvider != null );
        return commitProvider.GetCommit( monitor, parent, linkType is BranchLinkType.CI )?.Commit;
    }

    /// <summary>
    /// Ensure that the <see cref="GitBranch"/> exists: creating it at <see cref="GetStartCommit(IActivityMonitor)"/>
    /// if needed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool EnsureExists( IActivityMonitor monitor )
    {
        if( _link != null ) return true;
        // The branch provided by the user doesn't exist: the link type decides where it starts.
        var startCommit = GetStartCommit( monitor );
        if( startCommit == null ) return false;
        Throw.DebugAssert( """
                    Nothing could have fetched or create the branch since the HotBranch has been created
                    (GetBranch has been called - an existing remote would have created the local).
                    """, Repo.GitRepository.GetBranch( monitor, _name.Name, CK.Core.LogLevel.None ) == null );
        var gitBranch = Repo.GitRepository.EnsureIntegratedBranch( monitor, _name.Name, startCommit );
        return gitBranch != null && Refresh( monitor );
    }

    /// <summary>
    /// Close this branch. <see cref="Exists"/> must be true and this must not be the root branch.
    /// On success <see cref="GitBranch"/> has been integrated in the "dev/" branch of the closest existing parent
    /// (created if needed) and deleted: it becomes null and Exists is false. The base branch of the parent is not
    /// touched: a base branch only moves when its "dev/" branch is integrated.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="versionResolver">
    /// Optional provider of the resolver of the package versions that conflict when merging into the parent (see
    /// <see cref="PackageVersionMerge"/>): it must resolve them the way a build of the parent would.
    /// </param>
    /// <param name="onPreparedMerge">
    /// Not null to leave the merge in progress in the working folder when it conflicts beyond the package versions (the
    /// parent's "dev/" branch is checked out): it receives the <see cref="PreparedMerge"/>. This branch is not deleted:
    /// once the merge is committed, closing it again finds it merged and deletes it.
    /// </param>
    /// <returns>True on success, false otherwise.</returns>
    public bool Close( IActivityMonitor monitor,
                       Func<IActivityMonitor, IPackageVersionResolver?>? versionResolver = null,
                       Action<PreparedMerge>? onPreparedMerge = null )
    {
        Throw.CheckState( Exists && !BranchName.IsRoot );
        if( _link == null ) return true;
        // The closest branch must be searched from the PARENT: this branch exists (Exists is true), so
        // searching from itself answers itself - the branch would be merged into itself and then deleted
        // while it is the current HEAD.
        Throw.DebugAssert( _name.Parent != null );
        var closest = _info.GetRequiredClosestExistingBranch( monitor, _name.Parent );
        if( closest == null ) return false;
        Throw.DebugAssert( closest.Exists );

        if( _gitDevBranch != null && !IntegrateDevBranch( monitor ) )
        {
            return false;
        }
        var git = Repo.GitRepository;
        var branch = GitBranch;
        var target = closest.EnsureDevBranch();
        if( !MergeInto( monitor, ref target, branch, branch.Tip, versionResolver, onPreparedMerge ) )
        {
            return false;
        }
        if( branch.IsCurrentRepositoryHead && !git.Checkout( monitor, target ) )
        {
            return false;
        }
        if( !git.DeleteBranch( monitor, branch, DeleteGitBranchMode.WithTrackedBranch ) )
        {
            return false;
        }
        _link = null;
        return closest.Refresh( monitor );
    }

    /// <summary>
    /// Ensures that this <see cref="GitBranch"/> and <see cref="GitDevBranch"/> are synchronized with their
    /// remote origin counterparts (if any) and with the <see cref="BranchModelInfo.GetClosestExistingBranch(BranchName)"/>
    /// according to <see cref="BranchName.LinkType"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="applyLink">Optional link type to consider. By default, this configured <see cref="BranchName.LinkType"/> is considered.</param>
    /// <param name="versionResolver">
    /// Optional provider of the resolver of the package versions that conflict when merging the link commit (see
    /// <see cref="PackageVersionMerge"/>). It is called only when such a merge conflicts and only on project files;
    /// returning null (that must have logged why) fails the synchronization. Without it, any conflict fails it.
    /// </param>
    /// <param name="onPreparedMerge">
    /// Not null to leave the merge in progress in the working folder when it conflicts beyond the package versions (requires
    /// a <paramref name="versionResolver"/>, see <see cref="PackageVersionMerge.PrepareMerge"/>): the "dev/" branch is
    /// checked out, this receives the <see cref="PreparedMerge"/> and a person resolves the remaining conflicts and commits.
    /// The synchronization fails.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public bool Synchronize( IActivityMonitor monitor,
                             BranchLinkType applyLink = BranchLinkType.None,
                             Func<IActivityMonitor, IPackageVersionResolver?>? versionResolver = null,
                             Action<PreparedMerge>? onPreparedMerge = null )
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
        if( !parent.Synchronize( monitor, BranchLinkType.Manual ) )
        {
            return false;
        }
        // Make sure that the link commit is integrated into this branch. If not, it is merged into this "dev/"
        // branch: we always target the "dev/" branch if a merge must be done but the "dev/" may not exist.
        var linkCommit = GetLinkCommit( monitor, parent, applyLink );
        if( linkCommit == null )
        {
            return false;
        }
        var thisBranch = _gitDevBranch ?? GitBranch;
        var d = Repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( linkCommit, thisBranch.Tip );
        if( d.AheadBy is not 0 )
        {
            var dev = EnsureDevBranch();
            // Full merges the parent's branch rather than its tip commit: the merge then refuses a checked out
            // and dirty parent and names the branch in its commit message.
            var parentBranch = applyLink is BranchLinkType.Full ? parent.GitDevBranch ?? parent.GitBranch : null;
            if( !MergeInto( monitor, ref dev, parentBranch, linkCommit, versionResolver, onPreparedMerge ) || !Refresh( monitor ) )
            {
                return false;
            }
        }
        return true;
    }

    // Merges "other" into the "target" branch: nothing when the target already contains it, a fast-forward, or a merge
    // commit (no empty one: see GitRepository.MergeBranchContent). A merge commit is where the package versions may
    // conflict: with a resolver, they are aligned (see PackageVersionMerge) and, when other conflicts remain, the merge
    // can be left in progress in the working folder. When "otherBranch" is not null, it is the branch whose tip is
    // "other": it names the merge and a checked out and dirty one is refused.
    bool MergeInto( IActivityMonitor monitor,
                    ref Branch target,
                    Branch? otherBranch,
                    Commit other,
                    Func<IActivityMonitor, IPackageVersionResolver?>? versionResolver,
                    Action<PreparedMerge>? onPreparedMerge )
    {
        var git = Repo.GitRepository;
        var d = git.Repository.ObjectDatabase.CalculateHistoryDivergence( other, target.Tip );
        if( d.AheadBy is 0 ) return true;
        if( versionResolver == null || d.BehindBy is 0 || target.Tip.Tree.Sha == other.Tree.Sha )
        {
            return otherBranch != null
                    ? git.MergeBranchContent( monitor, ref target, otherBranch )
                    : git.MergeBranchContent( monitor, ref target, other );
        }
        if( otherBranch != null && otherBranch.IsCurrentRepositoryHead && !git.CheckCleanCommit( monitor ) )
        {
            return false;
        }
        var otherName = otherBranch != null
                            ? $"branch '{otherBranch.FriendlyName}'"
                            : $"commit '{other.Sha.AsSpan( 0, 7 )} {other.MessageShort}'";
        var merge = PackageVersionMerge.CreateMergeCommit( monitor,
                                                           git,
                                                           target.Tip,
                                                           other,
                                                           target.FriendlyName,
                                                           otherName,
                                                           () => versionResolver( monitor ),
                                                           out var aligned,
                                                           // When the merge is prepared, its conflicts are reported.
                                                           onPreparedMerge != null ? CK.Core.LogLevel.Trace : CK.Core.LogLevel.Error );
        if( merge == null )
        {
            // Nothing is merged until the person commits the merge: this fails either way.
            if( onPreparedMerge != null )
            {
                PrepareMerge( monitor, target, other, otherName, versionResolver, onPreparedMerge );
            }
            return false;
        }
        if( aligned.Count > 0 )
        {
            monitor.Info( $"""
                Merging {otherName} into '{target.FriendlyName}' in '{Repo.DisplayPath}' aligned {aligned.Count} package version(s):
                {aligned.Select( a => $"{a.PackageId}: {a.Ours} / {a.Theirs} => {a.Resolved}" ).Concatenate( Environment.NewLine )}
                """ );
        }
        // The merge commit is created in the object database: the target branch is fast-forwarded to it, which is
        // what handles a checked out target.
        return git.MergeBranchContent( monitor, ref target, merge );
    }

    // Leaves the merge in progress in the working folder (see PackageVersionMerge.PrepareMerge): the target branch is
    // checked out first. On success, the command reports the PreparedMerge (the log file has the details); a failure
    // is logged as an error.
    void PrepareMerge( IActivityMonitor monitor,
                       Branch target,
                       Commit other,
                       string otherName,
                       Func<IActivityMonitor, IPackageVersionResolver?> versionResolver,
                       Action<PreparedMerge> onPreparedMerge )
    {
        var git = Repo.GitRepository;
        if( !target.IsCurrentRepositoryHead )
        {
            if( !git.Checkout( monitor, target ) ) return;
            monitor.Info( $"'{target.FriendlyName}' is checked out in '{Repo.DisplayPath}' to resolve its conflicts." );
            target = git.Repository.Branches[target.FriendlyName];
        }
        if( PackageVersionMerge.PrepareMerge( monitor, git, target, other, otherName, () => versionResolver( monitor ), out var conflicts ) )
        {
            monitor.Info( $"""
                Merging {otherName} into '{target.FriendlyName}' in '{Repo.DisplayPath}' conflicts beyond the package versions.
                The merge is in progress in the working folder, the conflicts are in:
                {conflicts.Concatenate( Environment.NewLine )}
                """ );
            onPreparedMerge( new PreparedMerge( Repo, target.FriendlyName, otherName, conflicts ) );
        }
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
