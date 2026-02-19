using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Core;

/// <summary>
/// Encapsulates LibGit2Sharp <see cref="Repository"/>.
/// </summary>
public sealed partial class GitRepository : IDisposable
{
    static readonly StatusOptions _checkDirtyOptions = new StatusOptions() { IncludeIgnored = false };
    readonly GitRepositoryKey _repositoryKey;
    readonly Repository _git;
    readonly NormalizedPath _displayPath;
    readonly NormalizedPath _workingFolder;
    readonly List<string> _deferredPushRefSpecs;
    Signature _committer;

    GitRepository( GitRepositoryKey repositoryKey,
                   Signature committer,
                   Repository libRepository,
                   in NormalizedPath fullPath,
                   in NormalizedPath displayPath )
    {
        Throw.CheckNotNullArgument( repositoryKey );
        Throw.CheckNotNullArgument( libRepository );

        _repositoryKey = repositoryKey;
        _committer = committer;
        _git = libRepository;
        _workingFolder = fullPath;
        _displayPath = displayPath;
        _deferredPushRefSpecs = new List<string>();

        Throw.CheckArgument( _workingFolder.Path.Equals( new NormalizedPath( libRepository.Info.WorkingDirectory ), StringComparison.OrdinalIgnoreCase ) );
        Throw.CheckArgument( _workingFolder.Path.EndsWith( _displayPath, StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Disposes the LibGit repository.
    /// </summary>
    public void Dispose()
    {
        _git.Dispose();
    }

    /// <summary>
    /// Gets the <see cref="GitRepositoryKey"/>.
    /// </summary>
    public GitRepositoryKey RepositoryKey => _repositoryKey;

    /// <summary>
    /// The short path to display.
    /// </summary>
    public NormalizedPath DisplayPath => _displayPath;

    /// <summary>
    /// Gets the full physical path of the working folder.
    /// Same as LibGit's <see cref="RepositoryInformation.WorkingDirectory"/>.
    /// </summary>
    public NormalizedPath WorkingFolder => _workingFolder;

    /// <summary>
    /// Gets the current branch name (name of the repository's HEAD).
    /// </summary>
    public string CurrentBranchName => _git.Head.FriendlyName;

    /// <summary>
    /// Gets or sets the signature to use when modifying this repository.
    /// </summary>
    public Signature Committer
    {
        get => _committer;
        set
        {
            Throw.CheckNotNullArgument( value );
            _committer = value;
        }
    }

    /// <summary>
    /// Gets a mutable list of ref specs (see <see href="<see href="https://git-scm.com/book/en/v2/Git-Internals-The-Refspec"/>"/>)
    /// that will be implicitly pushed with the next push that will be done (can be <see cref="PushBranch(IActivityMonitor, Branch, bool)"/>,
    /// or <see cref="PushTags(IActivityMonitor, IEnumerable{string}, string)"/>, etc.). Once pushed, this list is cleared.
    /// <para>
    /// This has been designed to handle the update of the "ckli-repo" tag: instead of requiring each Repo initialization to
    /// systematically blindly push the "ckli-repo" tag (that costs and is 99.9% useless) or fetch the remote tags (that is a
    /// costly <c>git_remote_ls</c> operation) to "cleverly" push the the "ckli-repo" only when needed, this deferring systematically
    /// pushes the local "ckli-repo" tag among the first other references to push.
    /// </para>
    /// <para>
    /// This enables purely local scenario without overhead and guaranties that the "ckli-repo" tag is properly initialized on
    /// any "ckli aware repository".
    /// </para>
    /// </summary>
    public List<string> DeferredPushRefSpecs => _deferredPushRefSpecs;

    /// <summary>
    /// Gets the LibGit2Sharp <see cref="Repository"/>.
    /// This should be used when the simplified API that this wrapper offers is not enough.
    /// <para>
    /// This object MUST NOT be disposed.
    /// </para>
    /// </summary>
    public Repository Repository => _git;

    /// <summary>
    /// Captures minimal status information.
    /// </summary>
    /// <param name="CurrentBranchName">The currently checked out branch.</param>
    /// <param name="IsDirty">Whether the working folder is dirty.</param>
    /// <param name="CommitAhead">
    /// The number of commit that are ahead of the origin.
    /// 0 mean that there a no commit ahead of origin (there's nothing to push).
    /// Null if there is no origin (the branch is not tracked).
    /// </param>
    /// <param name="CommitBehind">
    /// Gets the number of commits that exist in origin but don't exist in this local one.
    /// 0 mean that there's no missing commit (there's nothing to pull).
    /// Null if there is no origin (the branch is not tracked).
    /// </param>
    public readonly record struct SimpleStatusInfo( string CurrentBranchName, bool IsDirty, int? CommitAhead, int? CommitBehind )
    {
        /// <summary>
        /// Gets whether this status is the <c>default</c>, uninitialized value.
        /// </summary>
        public bool IsDefault => CurrentBranchName == null;

        /// <summary>
        /// Gets whether the <see cref="CurrentBranchName"/> is tracked.
        /// </summary>
        [MemberNotNullWhen( true, nameof( CommitAhead ), nameof( CommitBehind ) )]
        public bool IsTracked => CommitAhead.HasValue;
    }

    /// <summary>
    /// Gets a <see cref="SimpleStatusInfo"/> for this repository.
    /// </summary>
    /// <returns>The simplified status info</returns>
    public SimpleStatusInfo GetSimpleStatusInfo()
    {
        var branchDetails = _git.Head.TrackingDetails;
        return new SimpleStatusInfo()
        {
            CurrentBranchName = _git.Head.FriendlyName,
            CommitAhead = branchDetails.AheadBy,
            CommitBehind = branchDetails.BehindBy,
            IsDirty = _git.RetrieveStatus( _checkDirtyOptions ).IsDirty,
        };
    }

    /// <summary>
    /// Checks that the current head is a clean commit (working directory is clean and no staging files exists).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True if the current head is clean, false otherwise.</returns>
    public bool CheckCleanCommit( IActivityMonitor monitor )
    {
        if( _git.RetrieveStatus( _checkDirtyOptions ).IsDirty )
        {
            monitor.Error( $"Repository '{DisplayPath}' has uncommitted changes ({CurrentBranchName})." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Gets the LibGitRepository's <see cref="Branch"/> of the given branch or null if the branch
    /// doesn't exist locally and in the "origin" remote.
    /// <para>
    /// If the remote "origin" exists, it is created locally and tracks the origin remote branch.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branchName">The branch name. Must not be null or white space.</param>
    /// <param name="missingLocalAndRemote">
    /// By default a warning is emitted if the branch doesn't exist locally nor on the origin.
    /// Use <see cref="LogLevel.None"/> to not warn.
    /// </param>
    /// <returns>The branch or null.</returns>
    public Branch? GetBranch( IActivityMonitor monitor, string branchName, LogLevel missingLocalAndRemote = LogLevel.Warn )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
        return DoGetBranch( monitor, _git, branchName, missingLocalAndRemote, _displayPath );
    }

    static Branch? DoGetBranch( IActivityMonitor monitor,
                                Repository r,
                                string branchName,
                                LogLevel missingLocalAndRemote,
                                string repoDisplayName )
    {
        var b = r.Branches[branchName];
        if( b == null )
        {
            string remoteName = "origin/" + branchName;
            var remote = r.Branches[remoteName];
            if( remote == null )
            {
                if( missingLocalAndRemote != LogLevel.None )
                {
                    var msg = $"Repository '{repoDisplayName}': Both local '{branchName}' and remote '{remoteName}' not found.";
                    monitor.Log( missingLocalAndRemote, msg );
                }
                return null;
            }
            monitor.Info( $"Creating local branch on remote '{remoteName}' in repository '{repoDisplayName}'." );
            b = r.Branches.Add( branchName, remote.Tip );
            b = r.Branches.Update( b, u => u.TrackedBranch = remote.CanonicalName );
        }
        return b;
    }

    static Branch DoEnsureBranch( IActivityMonitor monitor,
                                  Repository r,
                                  string branchName,
                                  LogLevel createLocalLogLevel,
                                  string repoDisplayName,
                                  out bool localCreated )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
        localCreated = false;
        var b = DoGetBranch( monitor, r, branchName, LogLevel.Warn, repoDisplayName: repoDisplayName );
        if( b == null )
        {
            localCreated = true;
            monitor.Log( createLocalLogLevel, $"Branch '{branchName}' does not exist. Creating purely local branch." ); ;
            b = r.CreateBranch( branchName );
        }
        return b;
    }

    /// <summary>
    /// Ensures that a local branch exists. If a remote branch from the 'origin' remote is known locally
    /// it will be associated.
    /// <para>
    /// If the branch is created without a remote, it will point at the current <see cref="Head"/>'s commit.
    /// The branch is guaranteed to exist but the <see cref="CurrentBranchName"/> stays where it is.
    /// Use <see cref="Checkout(IActivityMonitor, Branch)"/> to switch the head onto the branch.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branchName">The branch name.</param>
    /// <param name="createLocalLogLevel">Log level to log branch creation.</param>
    /// <returns>The branch.</returns>
    public Branch EnsureBranch( IActivityMonitor monitor, string branchName, LogLevel createLocalLogLevel = LogLevel.Info )
    {
        return DoEnsureBranch( monitor, _git, branchName, createLocalLogLevel, DisplayPath, out var _ );
    }

    /// <summary>
    /// Gets whether the head can be amended: the current branch
    /// is not tracked or the current commit is ahead of the remote branch.
    /// </summary>
    public bool CanAmendCommit => (_git.Head.TrackingDetails.AheadBy ?? 1) > 0;

    /// <summary>
    /// Fetches 'origin' (or all remotes) branches and optionally tags into this repository.
    /// <para>
    /// This uses the configured "fetch" ref specs (in the '.git/configuration' file) that defaults
    /// (for the default 'origin' remote) to the single "+refs/heads/*:refs/remotes/origin/*".
    /// See <see href="https://git-scm.com/book/en/v2/Git-Internals-The-Refspec"/>.
    /// <list type="bullet">
    ///     <item>When <paramref name="branchSpec"/> is null or empty. The ref specs are used as-is.</item>
    ///     <item>
    ///     When <paramref name="branchSpec"/> is specified. The '*' in all the ref specs are replaced with the branchSpec string,
    ///     whatever it is: it may end with a '*' (partial globs) to cover more than one branch.
    ///     </item>
    /// </list>
    /// This is a simple mechanism but powerful enough for our needs.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="withTags">
    /// Specify whether tags that point to fetched objects must be fetched from remote.
    /// When true, locally modified tags are lost.
    /// </param>
    /// <param name="originOnly">False to fetch all the remote branches. By default, only the 'origin' remote is considered.</param>
    /// <param name="branchSpec">Optional branch specification. Example: "fix/v3.*".</param>
    /// <returns>True on success, false on error.</returns>
    public bool FetchRemoteBranches( IActivityMonitor monitor, bool withTags, bool originOnly, string? branchSpec = null )
    {
        if( string.IsNullOrEmpty( branchSpec ) )
        {
            branchSpec = null;
        }
        using( monitor.OpenInfo( $"Fetching {(branchSpec == null ? "all ": "")
                                   }branches {(branchSpec != null ? $"like '{branchSpec}' " : "")
                                   }from '{DisplayPath
                                   }' {(originOnly ? "origin" : "all remotes")
                                   } with{(withTags ? "" : "out")
                                   } tags." ) )
        {
            try
            {
                if( !_repositoryKey.AccessKey.GetReadCredentials( monitor, out var creds ) ) return false;

                foreach( Remote remote in _git.Network.Remotes.Where( r => !originOnly || r.Name == "origin" ) )
                {
                    var logMsg = $"Fetching remote '{remote.Name}'.";
                    if( !originOnly ) monitor.Info( logMsg );
                    IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select( x => x.Specification );
                    if( branchSpec != null )
                    {
                        refSpecs = refSpecs.Select( r => r.Replace( "*", branchSpec ) );
                    }
                    Commands.Fetch( _git, remote.Name, refSpecs, new FetchOptions()
                    {
                        CredentialsProvider = ( url, user, types ) => creds,
                        TagFetchMode = withTags ? TagFetchMode.Auto : TagFetchMode.None
                    }, logMsg );
                }
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( "Error while fetching. This requires a manual fix.", ex );
                return false;
            }
        }
    }

    /// <summary>
    /// Fetches a branch from its remote if possible.
    /// <list type="bullet">
    ///     <item>
    ///     If the branch doesn't exist yet, a local branch bound to its 'origin' remote is created.
    ///     If the remote branch doesn't exist, the output <paramref name="branch"/> is null and this is not an error:
    ///     if you want the local branch to exist, call <see cref="EnsureBranch(IActivityMonitor, string, LogLevel)"/>
    ///     before.
    ///     </item>
    ///     <item>
    ///     If the local branch exists but is not tracked, the 'origin' remote is looked up for this branch and if it
    ///     exists on the remote, the output <paramref name="branch"/> is updated with the tracked branch.
    ///     </item>
    ///     <item>
    ///     If the branch exists and is already tracked (whatever the remote is), the output <paramref name="branch"/>
    ///     is updated with the tracked branch.
    ///     </item>
    /// </list>
    /// This doesn't create a local branch if the branch doesn't already exist locally or in the 'origin' remote.
    /// The output <paramref name="branch"/> is up-to-date: it can be null when the branch doesn't exist, and its <see cref="Branch.TrackedBranch"/>
    /// can be null and even can become null if the tracked branch disappeared from the remote.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="branchName">The human friendly branch name.</param>
    /// <param name="withTags">True to fetch the tags that point to any fetched object. False to not retrieve any remote tag.</param>
    /// <param name="branch">On success, contains an up-to-date branch instance.</param>
    /// <returns>True on success, false on error.</returns>
    public bool FetchRemoteBranch( IActivityMonitor monitor, string branchName, bool withTags, out Branch? branch )
    {
        branch = GetBranch( monitor, branchName, LogLevel.None );
        if( branch == null )
        {
            // The branch doesn't exist locally (as a local branch or as a known "origin/" branch).
            // We try to fetch the remote branch from 'origin' and then use GetBranch to create it
            // if it has been found on the 'origin' remote.
            if( !FetchFromOrigin( monitor, branchName, withTags ) )
            {
                branch = null;
                return false;
            }
            branch = GetBranch( monitor, branchName, LogLevel.None );
            return true;
        }
        else
        {
            var tracked = branch.TrackedBranch;
            if( tracked == null )
            {
                // The branch exists locally but is not bound to a tracked branch.
                // Same as above, we try to fetch the 'origin' one.
                if( !FetchFromOrigin( monitor, branchName, withTags ) )
                {
                    // On error, we let the local branch instance be returned.
                    return false;
                }
                // We bind the branch if the remote 'origin' has been found. 
                tracked = _git.Branches[$"refs/remotes/origin/{branchName}"];
                if( tracked != null )
                {
                    branch = _git.Branches.Update( branch, u => u.TrackedBranch = tracked.CanonicalName );
                }
                return true;
            }
            else if( tracked.Reference.IsRemoteTrackingBranch )
            {
                // The branch is a "refs/remotes/" branch.
                if( !DoFetch( monitor, this, tracked.RemoteName, branch.CanonicalName, tracked.CanonicalName, withTags ) )
                {
                    return false;
                }
                // We rebind the tracked branch: if it doesn't exist anymore on the the remote,
                // this clears the association.
                tracked = _git.Branches[tracked.CanonicalName];
                branch = _git.Branches.Update( branch, u => u.TrackedBranch = tracked?.CanonicalName );
                return true;
            }
            else
            {
                // The branch tracks a non remote branch: we cannot "fetch" anything.
                return true;
            }
        }

        bool FetchFromOrigin( IActivityMonitor monitor, string branchName, bool withTags )
        {
            return DoFetch( monitor,
                            this,
                            "origin",
                            src: $"refs/heads/{branchName}",
                            dst: $"refs/remotes/origin/{branchName}",
                            withTags );
        }

        static bool DoFetch( IActivityMonitor monitor,
                             GitRepository r,
                             string remoteName,
                             string src,
                             string dst,
                             bool withTags )
        {
            var refSpec = $"+{src}:{dst}";
            try
            {
                if( !r._repositoryKey.AccessKey.GetReadCredentials( monitor, out var creds ) ) return false;
                Commands.Fetch( r._git, remoteName, [refSpec], new FetchOptions()
                {
                    CredentialsProvider = ( url, user, types ) => creds,
                    TagFetchMode = withTags ? TagFetchMode.Auto : TagFetchMode.None
                }, null );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Error while fetching '{refSpec}'.", ex );
                return false;
            }
            return true;
        }

    }

    /// <summary>
    /// Checks out the specified local branch.
    /// If the current head has the same <see cref="Reference.CanonicalName"/>, nothing is done.
    /// Otherwise, the working folder must be clean (<see cref="CheckCleanCommit(IActivityMonitor)"/>)
    /// and the branch is checked out.
    /// <para>
    /// If the <paramref name="localBranch"/> is a remote branch this throws an <see cref="ArgumentException"/>:
    /// we should never check out a remote branch, only a local branch (that may track a remote one).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="localBranch">The local branch to check out.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Checkout( IActivityMonitor monitor, Branch localBranch )
    {
        Throw.CheckArgument( !localBranch.Reference.IsRemoteTrackingBranch );

        if( _git.Head.CanonicalName == localBranch.CanonicalName )
        {
            return true;
        }
        if( !CheckCleanCommit( monitor ) )
        {
            return false;
        }
        try
        {
            Commands.Checkout( _git, localBranch );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Checking out '{DisplayPath}/{localBranch.FriendlyName}' failed.", ex );
            return false;
        }
    }

    /// <summary>
    /// Gets the LibGit <see cref="Remote"/> and <see cref="UsernamePasswordCredentials"/> to use for a
    /// network operation.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="remoteName">The remote name.</param>
    /// <param name="forWrite">True to obtain the write credentials, false for reading.</param>
    /// <param name="remote">The remote on success.</param>
    /// <param name="creds">The credentials to use: can be null if no credentials are required.</param>
    /// <returns>True on success, false on error.</returns>
    public bool GetRemote( IActivityMonitor monitor,
                           string remoteName,
                           bool forWrite,
                           [NotNullWhen( true )] out Remote? remote,
                           out UsernamePasswordCredentials? creds )
    {
        if( (forWrite && !_repositoryKey.AccessKey.GetWriteCredentials( monitor, out creds ))
            || !_repositoryKey.AccessKey.GetReadCredentials( monitor, out creds ) )
        {
            remote = null;
            return false;
        }
        remote = _git.Network.Remotes[remoteName];
        if( remote == null )
        {
            monitor.Error( $"""
                    Unknown remote '{remoteName}'.
                    Defined remotes are: '{_git.Network.Remotes.Select( r => r.Name ).Concatenate("', '")}'.
                    """ );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Calls <see cref="MergeTrackedBranch(IActivityMonitor, ref Branch)"/> for each existing branch that has a remote tracked branch
    /// on 'origin' (or on any remote if <paramref name="fromAllRemotes"/> is true).
    /// <para>
    /// This (the <see cref="MergeTrackedBranch(IActivityMonitor, ref Branch)"/> method actually) handles the currently checked
    /// out branch transparently.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="continueOnError">True to continue on error.</param>
    /// <param name="fromAllRemotes">True to consider all remotes, not only 'origin'.</param>
    /// <param name="branchSpec">
    /// Optional branch name filter that applies to the local branch name. Example: "fix/v3.*".
    /// See <see cref="FetchRemoteBranches(IActivityMonitor, bool, bool, string?)"/>.
    /// </param>
    /// <returns>True on success, false otherwise.</returns>
    public bool MergeTrackedBranches( IActivityMonitor monitor, bool continueOnError = false, bool fromAllRemotes = false, string? branchSpec = null )
    {
        if( string.IsNullOrEmpty( branchSpec ) )
        {
            branchSpec = null;
        }
        bool success = true;
        foreach( var b in _git.Branches )
        {
            var tracked = b.TrackedBranch;
            if( tracked != null
                && tracked.CanonicalName.StartsWith( "refs/remotes/", StringComparison.Ordinal )
                && (fromAllRemotes || tracked.CanonicalName.StartsWith( "refs/remotes/origin/", StringComparison.Ordinal ))
                && (branchSpec == null || FilterBranchSpec( branchSpec, b )) )
            {
                var refB = b;
                success &= MergeTrackedBranch( monitor, ref refB );
                if( !success && !continueOnError ) break;
            }
        }
        return success;

        static bool FilterBranchSpec( ReadOnlySpan<char> branchSpec, Branch b )
        {
            var n = b.CanonicalName.AsSpan();
            Throw.DebugAssert( n.StartsWith( "refs/heads/" ) );
            n = n.Slice( 11 );
            if( branchSpec[^1] == '*' )
            {
                return n.StartsWith( branchSpec[0..^2] );
            }
            return n.Equals( branchSpec, StringComparison.Ordinal ); 
        }
    }

    /// <summary>
    /// Merges the <see cref="Branch.TrackedBranch"/> into its tracking <paramref name="branch"/>.
    /// The branch's <see cref="Branch.IsTracking"/> must be true otherwise an <see cref="ArgumentException"/> is thrown.
    /// <para>
    /// This method can handle local tracked branch even if it is mainly used with remote branches.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="branch">The local branch. This is updated to the new Branch instance on success.</param>
    /// <returns>True on success, false on error.</returns>
    public bool MergeTrackedBranch( IActivityMonitor monitor, ref Branch branch )
    {
        Throw.CheckArgument( branch.TrackedBranch != null );
        var tracked = branch.TrackedBranch;
        if( branch.Tip.Sha == tracked.Tip.Sha )
        {
            return true;
        }

        bool isHead = branch.IsCurrentRepositoryHead;
        if( isHead && !CheckCleanCommit( monitor ) )
        {
            return false;
        }
        var localName = branch.FriendlyName;
        var trackedName = tracked.FriendlyName;
        Exception? exception = null;
        try
        {
            var result = _git.ObjectDatabase.MergeCommits( branch.Tip, tracked.Tip, new MergeTreeOptions() { SkipReuc = true, FailOnConflict = true } );
            if( result.Tree != null )
            {
                var c = _git.ObjectDatabase.CreateCommit( _committer,
                                                          _committer,
                                                          $"Merge branch '{trackedName}'.",
                                                          result.Tree,
                                                          [branch.Tip, tracked.Tip],
                                                          prettifyMessage: true );
                if( isHead )
                {
                    // Before updating the branch, we need to detach the head.
                    // We do this as a no-op for the file system: the Tree is the same.
                    Commands.Checkout( _git, branch.Tip );
                }
                branch = _git.Branches.Add( localName, c, allowOverwrite: true );
                monitor.Trace( $"Branch '{trackedName}' has been merged into '{localName}' in '{DisplayPath}'." );
                if( isHead )
                {
                    branch = Commands.Checkout( _git, branch );
                }
                return true;
            }
        }
        catch( Exception ex )
        {
            exception = ex;
        }
        monitor.Error( $"Failed merging '{trackedName}' into '{localName}' in '{DisplayPath}'.", exception );
        return false;
    }

    /// <summary>
    /// Pushes the local's <see cref="Branch.TrackedBranch"/> to its remote.
    /// <para>
    /// if the <see cref="Branch.TrackedBranch"/> exists, its <see cref="Branch.RemoteName"/> must not be null
    /// otherwise it is an error.
    /// </para>
    /// <para>
    /// If the <paramref name="localBranch"/> is a remote branch this throws a <see cref="ArgumentException"/>.
    /// </para>
    /// <para>
    /// Whenever possible, a branch should always been fetched and merged before being pushed.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="localBranch">The local branch to push.</param>
    /// <param name="autoCreateRemoteBranch">
    /// True create the 'origin' remote branch if the branch has no tracked branch.
    /// <para>
    /// Caution: Before using this, the branch (or branches) must have been fetched.
    /// </para>
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public bool PushBranch( IActivityMonitor monitor, Branch localBranch, bool autoCreateRemoteBranch )
    {
        Throw.CheckArgument( !localBranch.Reference.IsRemoteTrackingBranch );

        var branchName = localBranch.FriendlyName;
        using( monitor.OpenInfo( $"Pushing branch '{DisplayPath}/{branchName}'." ) )
        {
            string? remoteName = null;
            var tracked = localBranch.TrackedBranch;
            if( tracked == null )
            {
                if( !autoCreateRemoteBranch )
                {
                    monitor.Error( $"Branch '{branchName}' has no tracked branch." );
                    return false;
                }
                monitor.Warn( $"Branch '{branchName}' has no tracked branch. Creating branch 'origin/{branchName}'." );
                remoteName = "origin";
                localBranch = _git.Branches.Update( localBranch, u => { u.Remote = remoteName; u.UpstreamBranch = localBranch.CanonicalName; } );
            }
            else if( tracked.RemoteName == null )
            {
                monitor.Error( $"Branch '{branchName}' tracks branch '{tracked.CanonicalName}' that is not a remote branch." );
                return false;
            }
            else
            {
                remoteName = tracked.RemoteName;
            }
            // When the branch has been "autoCreateRemoteBranch", then this is null: the branch will be pushed.
            int? aheadBy = localBranch.TrackingDetails.AheadBy;
            if( aheadBy.HasValue && aheadBy.Value == 0 )
            {
                monitor.CloseGroup( "Tracked branch is on the same commit. Push skipped." );
                return true;
            }
            if( !GetRemote( monitor, remoteName, forWrite: true, out var remote, out var creds ) )
            {
                return false;
            }
            return Push( monitor, remote, creds, [$"{localBranch.CanonicalName}:{localBranch.UpstreamBranchCanonicalName}"] );
        }
    }

    /// <summary>
    /// Low level push method that must be used whenever possible as this handles the <see cref="DeferredPushRefSpecs"/>.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="remote">The target remote.</param>
    /// <param name="creds">The credentials to use. See <see cref="GetRemote(IActivityMonitor, string, bool, out Remote?, out UsernamePasswordCredentials?)"/>.</param>
    /// <param name="pushRefSpecs">The ref specs to push.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Push( IActivityMonitor monitor, Remote remote, UsernamePasswordCredentials? creds, IEnumerable<string> pushRefSpecs )
    {
        if( _deferredPushRefSpecs.Count > 0 )
        {
            pushRefSpecs = pushRefSpecs.Concat( _deferredPushRefSpecs );
        }
        var commonLogMsg = $"'{DisplayPath}' references '{pushRefSpecs.Concatenate( "', '" )}'";
        using( monitor.OpenTrace( $"Pushing {commonLogMsg}." ) )
        {
            try
            {
                // Take no risk: consider that the error callback can be called concurrently
                // and that there may be more than one error.
                ConcurrentBag<string> errors = new ConcurrentBag<string>();
                var options = new PushOptions()
                {
                    CredentialsProvider = ( url, user, types ) => creds,
                    OnPushStatusError = ( e ) =>
                    {
                        errors.Add( $"""
                            Error while pushing ref '{e.Reference}':
                            {e.Message}
                            """ );
                    }
                };
                _git.Network.Push( remote, pushRefSpecs, options );
                if( !errors.IsEmpty )
                {
                    monitor.Error( $"""
                While pushing {commonLogMsg}':
                {errors.Concatenate( Environment.NewLine )}
                """ );
                    monitor.CloseGroup( $"{errors.Count} errors." );
                    return false;
                }
                _deferredPushRefSpecs.Clear();
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While pushing {commonLogMsg}.", ex );
                return false;
            }
        }
    }

    /// <summary>
    /// Commits any pending changes.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="commitMessage">
    /// Required commit message.
    /// This is ignored when <see cref="CommitBehavior.AmendIfPossibleAndKeepPreviousMessage"/> is used and <see cref="CanAmendCommit"/> is true.
    /// </param>
    /// <param name="commitBehavior">
    /// True to call <see cref="AmendCommit"/> if <see cref="CanAmendCommit"/>. is true.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public CommitResult Commit( IActivityMonitor monitor, string commitMessage, CommitBehavior commitBehavior = CommitBehavior.CreateNewCommit )
    {
        if( commitBehavior != CommitBehavior.CreateNewCommit && CanAmendCommit )
        {
            Func<string, string>? modified = null;
            switch( commitBehavior )
            {
                case CommitBehavior.CreateNewCommit:
                    Throw.InvalidOperationException();
                    break;
                case CommitBehavior.AmendIfPossibleAndKeepPreviousMessage:
                    modified = p => p;
                    break;
                case CommitBehavior.AmendIfPossibleAndAppendPreviousMessage:
                    Throw.CheckNotNullOrWhiteSpaceArgument( commitMessage );
                    modified = p => $"{commitMessage}(...)\r\n{p}";
                    break;
                case CommitBehavior.AmendIfPossibleAndPrependPreviousMessage:
                    Throw.CheckNotNullOrWhiteSpaceArgument( commitMessage );
                    modified = p => $"{p} (...)\r\n{commitMessage}";
                    break;
                case CommitBehavior.AmendIfPossibleAndOverwritePreviousMessage:
                    Throw.CheckNotNullOrWhiteSpaceArgument( commitMessage );
                    modified = p => commitMessage;
                    break;
            }
            return AmendCommit( monitor, modified );
        }
        Throw.CheckNotNullOrWhiteSpaceArgument( commitMessage );
        using( monitor.OpenInfo( $"Committing changes in '{DisplayPath}' (branch '{CurrentBranchName}')." ) )
        {
            Commands.Stage( _git, "*" );
            var s = _git.RetrieveStatus( _checkDirtyOptions );
            if( !s.IsDirty )
            {
                monitor.CloseGroup( "Working folder is up-to-date." );
                return CommitResult.NoChanges;
            }
            return DoCommit( monitor, commitMessage, _committer.When, false );
        }
    }

    /// <summary>
    /// Amends the current commit, optionally changing its message and/or its date.
    /// <see cref="CanAmendCommit"/> must be true otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="editMessage">
    /// Optional message transformer. By returning null, the operation is canceled and false is returned.
    /// </param>
    /// <param name="editDate">
    /// Optional date transformer. By returning null, the operation is canceled and false is returned.
    /// </param>
    /// <param name="skipIfNothingToCommit">
    /// By default, no amend is done if working folder is up to date.
    /// False will force the amend to be done if the date or message changed even if the working folder is clean.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public CommitResult AmendCommit( IActivityMonitor monitor,
                                     Func<string, string>? editMessage = null,
                                     Func<DateTimeOffset, DateTimeOffset?>? editDate = null,
                                     bool skipIfNothingToCommit = true )
    {
        if( !CanAmendCommit ) throw new InvalidOperationException( nameof( CanAmendCommit ) );
        using( monitor.OpenInfo( $"Amending Commit in '{DisplayPath}' (branch '{CurrentBranchName}')." ) )
        {
            var message = _git.Head.Tip.Message;
            if( editMessage != null ) message = editMessage( message );
            if( String.IsNullOrWhiteSpace( message ) )
            {
                monitor.CloseGroup( "Canceled by empty message." );
                return CommitResult.Error;
            }
            DateTimeOffset initialDate = _git.Head.Tip.Committer.When;
            DateTimeOffset? date = initialDate;
            if( editDate != null ) date = editDate( date.Value );
            if( date == null )
            {
                monitor.CloseGroup( "Canceled by null date." );
                return CommitResult.Error;
            }
            Commands.Stage( _git, "*" );
            var s = _git.RetrieveStatus( _checkDirtyOptions );
            bool hasChange = s.IsDirty;
            if( hasChange )
            {
                if( editDate == null )
                {
                    var minDate = initialDate.AddSeconds( 1 );
                    date = DateTimeOffset.Now;
                    if( date < minDate )
                    {
                        monitor.Trace( "Adjusted commit date to the next second." );
                        date = minDate;
                    }
                }
            }
            else
            {
                if( !skipIfNothingToCommit )
                {
                    bool messageUpdate = message != _git.Head.Tip.Message;
                    bool dateUpdate = date.Value != _git.Head.Tip.Committer.When;
                    if( messageUpdate && dateUpdate )
                    {
                        monitor.Info( "Updating message and date." );
                    }
                    else if( dateUpdate )
                    {
                        monitor.Info( "Updating commit date." );
                    }
                    else if( messageUpdate )
                    {
                        monitor.Info( "Only updating message." );
                    }
                    else skipIfNothingToCommit = true;
                }
                if( skipIfNothingToCommit )
                {
                    monitor.CloseGroup( "Working folder is up-to-date." );
                    return CommitResult.NoChanges;
                }
            }
            return DoCommit( monitor, message, date.Value, true );
        }
    }

    CommitResult DoCommit( IActivityMonitor monitor, string commitMessage, DateTimeOffset date, bool amendPreviousCommit )
    {
        try
        {
            Signature? author = amendPreviousCommit ? _git.Head.Tip.Author : _git.Config.BuildSignature( date );
            // Let AllowEmptyCommit even when amending: this avoids creating an empty commit.
            // If we are not amending, this is an error and we let the EmptyCommitException pops.
            var options = new CommitOptions { AmendPreviousCommit = amendPreviousCommit };
            try
            {
                Commit commit = _git.Commit( commitMessage, author ?? _committer, _committer, options );
                monitor.CloseGroup( "Committed changes." );
                return amendPreviousCommit ? CommitResult.Amended : CommitResult.Commited;
            }
            catch( EmptyCommitException )
            {
                if( !amendPreviousCommit ) throw;
                Throw.DebugAssert( "This check on merge commit is already done by LibGit2Sharp.", _git.Head.Tip.Parents.Count() == 1 );
                monitor.Trace( "No actual changes. Resetting branch to parent commit." );
                _git.Reset( ResetMode.Hard, _git.Head.Tip.Parents.Single() );
                Throw.DebugAssert( options.AmendPreviousCommit = true );
                string sha = _git.Head.Tip.Sha;
                _git.Commit( commitMessage, author ?? _committer, _committer, options );
                return sha == _git.Head.Tip.Sha ? CommitResult.NoChanges : CommitResult.Amended;
            }
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return CommitResult.Error;
        }
    }

    /// <summary>
    /// Resets the index to the tree recorded by the commit, updates the working directory to
    /// match the content of the index and by default tries to removes untracked files.
    /// <para>
    /// When removing of an untracked file fails, this returns true, warnings are emitted and
    /// <paramref name="remainingUntrackedFiles"/> is not null and contains the paths relative to
    /// the <see cref="WorkingFolder"/> that failed to be deleted.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="remainingUntrackedFiles">Outputs the untracked files that remains on the file system.</param>
    /// <param name="tryDeleteUntrackedFiles">False to not try to delete untracked files.</param>
    /// <returns>True on success, false on error.</returns>
    public bool ResetHard( IActivityMonitor monitor,
                           out List<string>? remainingUntrackedFiles,
                           bool tryDeleteUntrackedFiles = true )
    {
        using var _ = monitor.OpenInfo( $"Hard reset of '{DisplayPath}'." );
        remainingUntrackedFiles = null;
        try
        {
            _git.Reset( ResetMode.Hard );
            var status = _git.RetrieveStatus();
            int untrackedCount = status.Untracked.Count();
            if( untrackedCount > 0 )
            {
                if( tryDeleteUntrackedFiles )
                {
                    using( monitor.OpenTrace( $"Attempting to delete {untrackedCount} untracked files." ) )
                    {
                        foreach( var e in status.Untracked )
                        {
                            if( !FileHelper.DeleteFile( monitor, Path.Combine( _workingFolder, e.FilePath ) ) )
                            {
                                remainingUntrackedFiles ??= new List<string>();
                                remainingUntrackedFiles.Add( e.FilePath );
                            }
                        }
                        if( remainingUntrackedFiles != null )
                        {
                            monitor.Warn( $"""
                            Failed to delete untracked files:
                            {remainingUntrackedFiles.Concatenate( Environment.NewLine )}
                            """ );
                        }
                    }
                }
                else
                {
                    remainingUntrackedFiles = status.Untracked.Select( e => e.FilePath ).ToList();
                }
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return false;
        }
    }

    /// <summary>
    /// Checks out the specified branch, creating it if it doesn't exist and by default fetch-merge from the remote if
    /// it's a tracking branch.
    /// <list type="number">
    ///     <item>
    ///     If the current branch name is <paramref name="branchName"/>, it is fetch-merged from the remote (unless <paramref name="skipFetchMerge"/>
    ///     is true or it is not a tracking branch).
    ///     </item>
    ///     <item>Otherwise, the working folder must be clean (<see cref="CheckCleanCommit(IActivityMonitor)"/>).</item>
    ///     <item>
    ///     If the local branch already exists, it is checked out and fetch-merged from the remote (unless <paramref name="skipFetchMerge"/>
    ///     is true or it is not a tracking branch).</item>
    ///     <item>
    ///     Else (the local branch doesn't exist), all branches from 'origin' are fetched, a local branch is created
    ///     (an tracks its remote branch if it exists) and it is checked out.
    ///     </item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branchName">The branch name.</param>
    /// <param name="skipFetchMerge">True to NOT fetch-merge the head once checked out.</param>
    /// <returns>True on success, false on error.</returns>
    public bool FullCheckout( IActivityMonitor monitor, string branchName, bool skipFetchMerge = false )
    {
        try
        {
            if( CurrentBranchName != branchName )
            {
                if( !CheckCleanCommit( monitor ) ) return false;
                var b = DoGetBranch( monitor, _git, branchName, LogLevel.None, _displayPath );
                if( b == null )
                {
                    if( !FetchRemoteBranches( monitor, withTags: false, originOnly: true ) )
                    {
                        return false;
                    }
                    b = DoEnsureBranch( monitor, _git, branchName, LogLevel.Warn, _displayPath, out bool localCreated );
                    // Either the branch has been created from its remote fetched branch, or it has been created
                    // as a local branch (as there's no remote branch): in both cases, we can skip the pull.
                    skipFetchMerge = true;
                }
                monitor.Info( $"Checking out {branchName} (leaving {CurrentBranchName})." );
                Commands.Checkout( _git, b );
            }
            if( skipFetchMerge || _git.Head.TrackedBranch == null )
            {
                return true;
            }
            return skipFetchMerge || FetchMergeHead( monitor );
        }
        catch( Exception ex )
        {
            monitor.Fatal( "Unexpected error. Manual fix should be required.", ex );
            return false;
        }
    }

    /// <summary>
    /// Fetch-Merges (pulls) the current head from its tracked remote branch.
    /// Any merge conflict is an error with <see cref="MergeFileFavor.Normal"/> and this is the safest mode.
    /// Choosing one of other flavors will not trigger a conflict error.
    /// <para>
    /// Tags that point to the remote branch will be retrieved and will replace locally defined tags if they point
    /// to the same object. If a local tag points to a different object, this will be an error.
    /// To prevent this, <see cref="GetDiffTags(IActivityMonitor, out GitTagInfo.Diff?, string)"/> can be used
    /// to handle conflicting tags before calling this method.
    /// </para>
    /// <para>
    /// If the current head has no associated tracking branch, nothing is done. 
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="mergeFileFavor">How merge must be done.</param>
    /// <param name="fastForwardStrategy">The fast forward strategy to apply.</param>
    /// <returns>True on success, false on error.</returns>
    public bool FetchMergeHead( IActivityMonitor monitor,
                                MergeFileFavor mergeFileFavor = MergeFileFavor.Normal,
                                FastForwardStrategy fastForwardStrategy = FastForwardStrategy.Default )
    {
        if( _git.Head.TrackedBranch == null )
        {
            monitor.Warn( $"There is no tracking branch for the '{DisplayPath}/{CurrentBranchName}' branch. Skip pulling from the remote." );
            return true;
        }

        if( !_repositoryKey.AccessKey.GetReadCredentials( monitor, out var creds ) )
        {
            return false;
        }

        try
        {
            var result = Commands.Pull( _git, _committer, new PullOptions
            {
                FetchOptions = new FetchOptions
                {
                    // We don't want ALL the tags (GIT_REMOTE_DOWNLOAD_TAGS_ALL), only the
                    // tags that point to objects retrieved during this fetch (GIT_REMOTE_DOWNLOAD_TAGS_AUTO).
                    TagFetchMode = TagFetchMode.Auto,
                    CredentialsProvider = ( url, user, types ) => creds
                },
                MergeOptions = new MergeOptions
                {
                    MergeFileFavor = mergeFileFavor,
                    CommitOnSuccess = true,
                    FailOnConflict = true,
                    FastForwardStrategy = fastForwardStrategy,
                    SkipReuc = true
                }
            } );
            if( result.Status == MergeStatus.Conflicts )
            {
                monitor.Error( $"Unable to pull '{DisplayPath}/{CurrentBranchName}'. Merge conflicts must be manually fixed." );
                return false;
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While pulling from '{DisplayPath}/{CurrentBranchName}'.", ex );
            return false;
        }
    }



    /// <summary>
    /// Clones the <see cref="GitRepositoryKey.OriginUrl"/> in a local working folder
    /// that must be the 'origin' remote.
    /// <para>
    /// The remote repository can be totally empty: an initial empty commit is created in such case.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="committer">The signature to use when modifying the repository.</param>
    /// <param name="git">The Git key.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// (This is often the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
    /// </param>
    /// <returns>The GitRepository object or null on error.</returns>
    public static GitRepository? Clone( IActivityMonitor monitor,
                                        GitRepositoryKey git,
                                        Signature committer,
                                        NormalizedPath workingFolder,
                                        NormalizedPath displayPath )
    {
        var r = CloneWorkingFolder( monitor, git, workingFolder );
        return r == null ? null : new GitRepository( git, committer, r, workingFolder, displayPath );
    }

    /// <summary>
    /// Initializes a new Git repository in the specified working folder.
    /// <para>
    /// Unlike <see cref="Clone"/>, this creates a new repository locally rather than cloning from a remote.
    /// An "origin" remote is set to the specified URL (can be a file:// URL for local-only stacks).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secrets store for credentials.</param>
    /// <param name="committer">The signature to use when modifying the repository.</param>
    /// <param name="workingFolder">The local working folder to initialize.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// </param>
    /// <param name="isPublic">Whether this repository is public or private.</param>
    /// <param name="originUrl">The URL to set as the "origin" remote. Can be a file:// URL for local-only stacks.</param>
    /// <param name="branchName">The initial branch name. Defaults to "main".</param>
    /// <returns>The GitRepository object or null on error.</returns>
    public static GitRepository? Init( IActivityMonitor monitor,
                                       ISecretsStore secretsStore,
                                       Signature committer,
                                       NormalizedPath workingFolder,
                                       NormalizedPath displayPath,
                                       bool isPublic,
                                       Uri originUrl,
                                       string branchName = "main" )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckArgument( !workingFolder.IsEmptyPath );
        Throw.CheckArgument( !displayPath.IsEmptyPath );
        Throw.CheckNotNullArgument( originUrl );
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );

        using( monitor.OpenInfo( $"Initializing new repository at '{workingFolder}'." ) )
        {
            try
            {
                // Create the directory
                Directory.CreateDirectory( workingFolder );

                // Initialize the repository with the specified initial branch
                Repository.Init( workingFolder, isBare: false );

                var r = new Repository( workingFolder );

                // Set the default branch to the specified branchName (instead of "master")
                // For an unborn branch, we need to set HEAD as a symbolic reference
                r.Refs.Add( "HEAD", $"refs/heads/{branchName}", allowOverwrite: true );

                // Add origin remote
                r.Network.Remotes.Add( "origin", originUrl.AbsoluteUri );
                monitor.Info( $"Added 'origin' remote: {originUrl}" );

                var gitKey = new GitRepositoryKey( secretsStore, originUrl, isPublic );

                return new GitRepository( gitKey, committer, r, workingFolder, displayPath );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Failed to initialize repository at '{workingFolder}'.", ex );
                return null;
            }
        }
    }

    /// <summary>
    /// Opens a working folder. The <paramref name="workingFolder"/> must exist otherwise an error is logged.
    /// <para>
    /// When <paramref name="expectedOriginUrl"/> is not null, the current "origin" must be
    /// the same (case insensitive) or it is an error otherwise casing mismatch is fixed automatically.
    /// If there is no current "origin", it is created.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="committer">The signature to use when modifying the repository.</param>
    /// <param name="secretsStore">The key store to use.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// (This is often the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
    /// </param>
    /// <param name="isPublic">Whether this repository is a public or private one.</param>
    /// <param name="expectedOriginUrl">Optional expected "origin" url.</param>
    /// <returns>The SimpleGitRepository object or null on error.</returns>
    public static GitRepository? Open( IActivityMonitor monitor,
                                       ISecretsStore secretsStore,
                                       Signature committer,
                                       NormalizedPath workingFolder,
                                       NormalizedPath displayPath,
                                       bool isPublic,
                                       Uri? expectedOriginUrl = null )
    {
        var r = OpenWorkingFolder( monitor, workingFolder, warnOnly: false, expectedOriginUrl );
        if( r == null ) return null;

        var gitKey = new GitRepositoryKey( secretsStore, r.Value.OriginUrl, isPublic );
        return new GitRepository( gitKey, committer, r.Value.Repository, workingFolder, displayPath );
    }

    /// <summary>
    /// Clones the <see cref="GitRepositoryKey.OriginUrl"/> in a local working folder
    /// that must be the 'origin' remote.
    /// <para>
    /// The remote repository can be totally empty: an initial empty commit is created in such case.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="git">The Git key.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <returns>The LibGit2Sharp Repository object or null on error.</returns>
    public static Repository? CloneWorkingFolder( IActivityMonitor monitor,
                                                  GitRepositoryKey git,
                                                  NormalizedPath workingFolder )
    {
        using( monitor.OpenInfo( $"Cloning '{workingFolder}' from '{git.OriginUrl}'." ) )
        {
            if( !git.AccessKey.GetReadCredentials( monitor, out var creds ) ) return null;
            Repository? r = null;
            try
            {
                Repository.Clone( git.OriginUrl.AbsoluteUri, workingFolder, new CloneOptions()
                {
                    FetchOptions = { CredentialsProvider = ( url, user, cred ) => creds },
                    Checkout = true
                } );
                r = new Repository( workingFolder );
                EnsureFirstCommit( monitor, r );
                return r;
            }
            catch( Exception ex )
            {
                monitor.Error( "Git clone failed. Leaving existing directory as-is.", ex );
                r?.Dispose();
                return null;
            }
        }
    }

    /// <summary>
    /// Tries to open an existing working folder. An "origin" remote must exist.
    /// <para>
    /// When <paramref name="expectedOriginUrl"/> is not null, the current "origin" must be
    /// the same (case insensitive) or it is an error otherwise casing mismatch is fixed automatically.
    /// If there is no current "origin", it is created.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="workingFolder">The local working folder (above the .git folder).</param>
    /// <param name="warnOnly">True to emit only warnings on error, false to emit errors.</param>
    /// <param name="expectedOriginUrl">Optional expected "origin" url.</param>
    /// <returns>The LibGit2Sharp repository object and its "origin" Url or null on error.</returns>
    public static (Repository Repository, Uri OriginUrl)? OpenWorkingFolder( IActivityMonitor monitor,
                                                                             NormalizedPath workingFolder,
                                                                             bool warnOnly,
                                                                             Uri? expectedOriginUrl = null )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckArgument( !workingFolder.IsEmptyPath );
        Throw.CheckArgument( expectedOriginUrl == null || expectedOriginUrl.IsAbsoluteUri );
        try
        {
            var errorLevel = warnOnly ? LogLevel.Warn : LogLevel.Error;
            var gitFolderPath = workingFolder.AppendPart( ".git" );
            if( !Directory.Exists( gitFolderPath ) )
            {
                monitor.Log( errorLevel, $"The folder '{gitFolderPath}' does not exist." );
                return null;
            }
            if( !Repository.IsValid( gitFolderPath ) )
            {
                monitor.Log( errorLevel, $"Git folder '{gitFolderPath}' exists but is not a valid Repository. This must be fixed manually." );
                return null;
            }
            Uri? originUrl = null;
            var r = new Repository( workingFolder );
            var origin = r.Network.Remotes.FirstOrDefault( rem => rem.Name == "origin" );
            if( origin == null )
            {
                if( expectedOriginUrl == null )
                {
                    monitor.Log( errorLevel, $"""
                                          Existing '{workingFolder}' must have an 'origin' remote. Remotes are: '{r.Network.Remotes.Select( r => r.Name ).Concatenate( "', '" )}'.
                                          This must be fixed manually.
                                          """ );
                    r.Dispose();
                    return null;
                }
                origin = r.Network.Remotes.Add( "origin", expectedOriginUrl.ToString() );
                originUrl = expectedOriginUrl;
            }
            else
            {
                if( !Uri.TryCreate( origin.Url, UriKind.Absolute, out originUrl ) )
                {
                    monitor.Log( errorLevel, $"""
                                          Existing '{workingFolder}' has its 'origin' that is not a valid absolute Uri: '{origin.Url}'.
                                          This must be fixed manually.
                                          """ );
                    r.Dispose();
                    return null;
                }
                if( expectedOriginUrl != null )
                {
                    if( !GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer.Equals( expectedOriginUrl, originUrl ) )
                    {
                        monitor.Log( errorLevel, $"""
                                          Existing '{workingFolder}' has its 'origin' set to '{origin.Url}' but the expected origin is '{expectedOriginUrl}'.
                                          This must be fixed manually.
                                          """ );
                        r.Dispose();
                        return null;
                    }
                    if( !StringComparer.Ordinal.Equals( expectedOriginUrl.ToString(), origin.Url.ToString() ) )
                    {
                        monitor.Trace( $"Fixed case for origin url of '{workingFolder}' from '{origin.Url}' to '{expectedOriginUrl}'." );
                        r.Network.Remotes.Update( "origin", u => u.Url = expectedOriginUrl.ToString() );
                        originUrl = expectedOriginUrl;
                    }
                }
            }
            EnsureFirstCommit( monitor, r );
            return (r, originUrl);
        }
        catch( Exception ex )
        {
            monitor.Fatal( $"Failed to open Git '{workingFolder}'.", ex );
            return null;
        }
    }

    static void EnsureFirstCommit( IActivityMonitor m, Repository r )
    {
        if( !r.Commits.Any() )
        {
            m.Info( $"Uninitialized repository: automatically creating an initial commit." );
            var date = DateTimeOffset.Now;
            Signature author = r.Config.BuildSignature( date );
            var committer = new Signature( "CKli", "none", date );
            r.Commit( "Initial commit automatically created.", author, committer, new CommitOptions { AllowEmptyCommit = true } );
        }
    }

}
