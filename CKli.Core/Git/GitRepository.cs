using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Core;

/// <summary>
/// Encapsulates LibGit2Sharp <see cref="Repository"/>.
/// </summary>
public sealed class GitRepository : IGitHeadInfo, IDisposable
{
    static readonly StatusOptions _checkDirtyOptions = new StatusOptions() { IncludeIgnored = false };
    readonly GitRepositoryKey _repositoryKey;
    readonly Repository _git;
    readonly NormalizedPath _displayPath;
    readonly NormalizedPath _workingFolder;

    GitRepository( GitRepositoryKey repositoryKey,
                   Repository libRepository,
                   in NormalizedPath fullPath,
                   in NormalizedPath displayPath )
    {
        Throw.CheckNotNullArgument( repositoryKey );
        Throw.CheckNotNullArgument( libRepository );

        _repositoryKey = repositoryKey;
        _git = libRepository;
        _workingFolder = fullPath;
        _displayPath = displayPath;

        Throw.CheckArgument( _workingFolder == libRepository.Info.WorkingDirectory );
        Throw.CheckArgument( _workingFolder.EndsWith( _displayPath, strict: false ) );
    }

    /// <summary>
    /// Disposes the LibGit repository.
    /// </summary>
    public void Dispose()
    {
        _git.Dispose();
    }

    /// <summary>
    /// Gets whether the Git repository is public or private.
    /// </summary>
    public bool IsPublic => _repositoryKey.IsPublic;

    /// <summary>
    /// Gets the remote origin url.
    /// </summary>
    public Uri OriginUrl => _repositoryKey.OriginUrl;

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
    /// Gets the git provider kind.
    /// </summary>
    public KnownGitProvider KnownGitProvider => _repositoryKey.KnownGitProvider;

    /// <summary>
    /// Gets the head information.
    /// </summary>
    public IGitHeadInfo Head => this;

    string IGitHeadInfo.CommitSha => _git.Head.Tip.Sha;

    string IGitHeadInfo.Message => _git.Head.Tip.Message;

    DateTimeOffset IGitHeadInfo.CommitDate => _git.Head.Tip.Committer.When;

    int? IGitHeadInfo.AheadOriginCommitCount => _git.Head.TrackingDetails.AheadBy;

    string? IGitHeadInfo.GetSha( string? path )
    {
        if( string.IsNullOrEmpty( path ) ) return _git.Head.Tip.Sha;
        var e = _git.Head.Tip.Tree[path];
        return e?.Target.Sha;
    }

    /// <summary>
    /// Captures minimal status information.
    /// </summary>
    /// <param name="CurrentBranchName">The currently checked out branch.</param>
    /// <param name="IsDirty">Whether the working folder is dirty.</param>
    /// <param name="CommitAhead">
    /// The number of commit that are ahead of the origin.
    /// 0 mean that there a no commit ahead of origin (there's notthing to push).
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
        /// Gets whether this status is the <c>default</c>, unitialized value.
        /// </summary>
        public bool IsDefault => CurrentBranchName == null;
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
    /// <param name="m">The monitor to use.</param>
    /// <returns>True if the current head is clean, false otherwise.</returns>
    public bool CheckCleanCommit( IActivityMonitor m )
    {
        if( _git.RetrieveStatus( _checkDirtyOptions ).IsDirty )
        {
            m.Error( $"Repository '{DisplayPath}' has uncommitted changes ({CurrentBranchName})." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Gets the sha of the given branch tip or null if the branch doesn't exist.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branchName">The branch name. Must not be null or white space.</param>
    /// <returns>The Sha or null.</returns>
    public string? GetBranchSha( IActivityMonitor monitor, string branchName )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
        var b = DoGetBranch( monitor, _git, branchName, LogLevel.Warn, _displayPath );
        return b?.Tip.Sha;
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
    /// Ensures that a local branch exists. If a remote branch form the 'origin' remote is known locally
    /// it will be associated.
    /// <para>
    /// If the branch is created, it will point at the same commit as the current <see cref="Head"/>.
    /// The branch is guaranteed to exist but the <see cref="CurrentBranchName"/> stays where it is.
    /// Use <see cref="SetCurrentBranch(IActivityMonitor, string, bool)"/> to make sure that the potential remote 'origin' branch
    /// is fetched if it exists.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branchName">The branch name.</param>
    public void EnsureBranch( IActivityMonitor monitor, string branchName, LogLevel createLocalLogLevel = LogLevel.Info )
    {
        DoEnsureBranch( monitor, _git, branchName, createLocalLogLevel, DisplayPath, out var _ );
    }

    /// <summary>
    /// Gets whether the head can be amended: the current branch
    /// is not tracked or the current commit is ahead of the remote branch.
    /// </summary>
    public bool CanAmendCommit => (_git.Head.TrackingDetails.AheadBy ?? 1) > 0;

    /// <summary>
    /// Fetches 'origin' (or all remotes) branches and tags into this repository.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="originOnly">False to fetch all the remote branches. By default, branches from only 'origin' remote are considered.</param>
    /// <returns>True on success, false on error.</returns>
    public bool FetchBranches( IActivityMonitor monitor, bool originOnly = true )
    {
        using( monitor.OpenInfo( $"Fetching {(originOnly ? "origin" : "all remotes")} in repository '{DisplayPath}'." ) )
        {
            try
            {
                if( !_repositoryKey.GetReadCredentials( monitor, out var creds ) ) return false;

                foreach( Remote remote in _git.Network.Remotes.Where( r => !originOnly || r.Name == "origin" ) )
                {
                    if( !originOnly ) monitor.Info( $"Fetching remote '{remote.Name}'." );
                    IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select( x => x.Specification ).ToArray();
                    Commands.Fetch( _git, remote.Name, refSpecs, new FetchOptions()
                    {
                        CredentialsProvider = ( url, user, types ) => creds,
                        TagFetchMode = TagFetchMode.All
                    }, $"Fetching remote '{remote.Name}'." );
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
    /// Checks out the specified branch. 
    /// <list type="number">
    ///     <item>If <see cref="CurrentBranchName"/> is <paramref name="branchName"/>, does nothing.</item>
    ///     <item>Otherwise, the working folder must be clean (<see cref="CheckCleanCommit(IActivityMonitor)"/>).</item>
    ///     <item>If the local branch doesn't exist yet, all branches from 'origin' are fetched, a tracking local branch is created if the remote branch exists and it is checked out.</item>
    ///     <item>If the local branch already exists, it is checked out and pulled-merge from the remote unless <paramref name="skipPullMerge"/> is true.</item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branchName">The branch name.</param>
    /// <param name="skipPullMerge">True to not pull-merge the branch if it exists locally.</param>
    /// <returns>True on success, false on error.</returns>
    public bool SetCurrentBranch( IActivityMonitor monitor, string branchName, bool skipPullMerge = false )
    {
        if( CurrentBranchName == branchName ) return true;
        try
        {
            if( !CheckCleanCommit( monitor ) ) return false;
            var b = DoGetBranch( monitor, _git, branchName, LogLevel.None, _displayPath );
            if( b == null )
            {
                if( !FetchBranches( monitor, originOnly: true ) )
                {
                    return false;
                }
                b = DoEnsureBranch( monitor, _git, branchName, LogLevel.Warn, _displayPath, out bool localCreated );
                // Either the branch has been created from its remote fetched branch, or it has been created
                // a a local branch (as there's no remote branch): in both case, we can skip the pull.
                skipPullMerge = true;
            }
            monitor.Info( $"Checking out {branchName} (leaving {CurrentBranchName})." );
            Commands.Checkout( _git, b );
            return skipPullMerge || Pull( monitor, MergeFileFavor.Normal ).IsSuccess();
        }
        catch( Exception ex )
        {
            monitor.Fatal( "Unexpected error. Manual fix should be required.", ex );
            return false;
        }
    }

    /// <summary>
    /// Pull-Merge the current head from the remote. Any merge conflict is an error with <see cref="MergeFileFavor.Normal"/> and this is the
    /// safest mode. Choosing one of other flavors will not trigger a conflict error.
    /// <para>
    /// If the current head has no associated tracking branch, nothing is done. 
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="mergeFileFavor">How merge must be done.</param>
    /// <param name="fastForwardStrategy">The fast forward strategy to apply.</param>
    /// <returns>A MergeResult: both <see cref="MergeResult.Error"/> and <see cref="MergeResult.ErrorConflicts"/> are failures.</returns>
    public MergeResult Pull( IActivityMonitor monitor,
                             MergeFileFavor mergeFileFavor = MergeFileFavor.Normal,
                             FastForwardStrategy fastForwardStrategy = FastForwardStrategy.Default )
    {
        if( _git.Head.TrackedBranch == null )
        {
            monitor.Warn( $"There is no tracking branch for the '{DisplayPath}/{CurrentBranchName}' branch. Skip pulling from the remote." );
            return MergeResult.UpToDate;
        }

        if( !_repositoryKey.GetReadCredentials( monitor, out var creds ) ) return MergeResult.Error;

        var merger = _git.Config.BuildSignature( DateTimeOffset.Now ) ?? new Signature( "CKli", "none", DateTimeOffset.Now );
        try
        {
            var result = Commands.Pull( _git, merger, new PullOptions
            {
                FetchOptions = new FetchOptions
                {
                    TagFetchMode = TagFetchMode.All,
                    CredentialsProvider = (url, user, types) => creds
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
                monitor.Error( "Merge conflicts occurred. Unable to merge changes from the remote." );
                return MergeResult.ErrorConflicts;
            }
            return result.Status switch
            {
                MergeStatus.FastForward => MergeResult.FastForward,
                MergeStatus.NonFastForward => MergeResult.NonFastForward,
                MergeStatus.UpToDate => MergeResult.UpToDate,
                _ => Throw.NotSupportedException<MergeResult>()
            };
        }
        catch( Exception ex )
        {
            monitor.Error( $"While pulling from '{DisplayPath}/{CurrentBranchName}'.", ex );
            return MergeResult.Error;
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
            return DoCommit( monitor, commitMessage, DateTimeOffset.Now, false );
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
            var committer = new Signature( "CKli", "none", date );
            try
            {
                Commit commit = _git.Commit( commitMessage, author ?? committer, committer, options );
                monitor.CloseGroup( "Committed changes." );
                return amendPreviousCommit ? CommitResult.Amended : CommitResult.Commited;
            }
            catch( EmptyCommitException )
            {
                if( !amendPreviousCommit ) throw;
                Throw.DebugAssert( "This check on merge commit is already done by LibGit2Sharp.", _git.Head.Tip.Parents.Count() == 1 );
                monitor.Trace( "No actual changes. Reseting branch to parent commit." );
                _git.Reset( ResetMode.Hard, _git.Head.Tip.Parents.Single() );
                Throw.DebugAssert( options.AmendPreviousCommit = true );
                string sha = _git.Head.Tip.Sha;
                _git.Commit( commitMessage, author, committer, options );
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
    /// <see cref="remainingUntrackedFiles"/> is not null and contains the paths relative to
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
                    using( monitor.OpenTrace( $"ResetHard: Attempting to delete {untrackedCount} untracked files." ) )
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
    /// Gets whether <see cref="Push(IActivityMonitor)"/> can be called:
    /// the current branch is tracked and is ahead of the remote branch.
    /// </summary>
    public bool CanPush => (_git.Head.TrackingDetails.AheadBy ?? 0) > 0;

    /// <summary>
    /// Pushes changes from the current branch to the origin.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Push( IActivityMonitor monitor ) => Push( monitor, CurrentBranchName );

    /// <summary>
    /// Pushes changes from a branch to the origin.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="branchName">Local branch name.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Push( IActivityMonitor monitor, string branchName )
    {
        Throw.CheckNotNullArgument( branchName );
        using( monitor.OpenInfo( $"Pushing '{DisplayPath}' (branch '{branchName}') to origin." ) )
        {
            if( !_repositoryKey.GetWriteCredentials( monitor, out var creds ) ) return false;
            try
            {
                var b = _git.Branches[branchName];
                if( b == null )
                {
                    monitor.Error( $"Unable to find branch '{branchName}' in '{DisplayPath}'." );
                    return false;
                }
                bool created = false;
                if( !b.IsTracking )
                {
                    monitor.Warn( $"Branch '{branchName}' does not exist on the remote. Creating the remote branch on 'origin'." );
                    _git.Branches.Update( b, u => { u.Remote = "origin"; u.UpstreamBranch = b.CanonicalName; } );
                    created = true;
                }
                if( created || (b.TrackingDetails.AheadBy ?? 1) > 0 )
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
                    _git.Network.Push( b, options );
                    if( errors.Count > 0 )
                    {
                        foreach( var error in errors )
                        {
                            monitor.Error( error );
                        }
                        monitor.CloseGroup( $"{errors.Count} errors." );
                    }
                }
                else
                {
                    monitor.CloseGroup( "Remote branch is on the same commit. Push skipped." );
                }
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( ex );
                return false;
            }
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
    /// <param name="git">The Git key.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// (This is often the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
    /// </param>
    /// <returns>The GitRepository object or null on error.</returns>
    public static GitRepository? Clone( IActivityMonitor monitor,
                                        GitRepositoryKey git,
                                        NormalizedPath workingFolder,
                                        NormalizedPath displayPath )
    {
        var r = CloneWorkingFolder( monitor, git, workingFolder );
        return r == null ? null : new GitRepository( git, r, workingFolder, displayPath );
    }

    /// <summary>
    /// Opens a working folder. The <paramref name="workingFolder"/> must exist otherwise an error is logged.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The key store to use.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// (This is often the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
    /// </param>
    /// <param name="isPublic">Whether this repository is a public or private one.</param>
    /// <returns>The SimpleGitRepository object or null on error.</returns>
    public static GitRepository? Open( IActivityMonitor monitor,
                                       ISecretsStore secretsStore,
                                       NormalizedPath workingFolder,
                                       NormalizedPath displayPath,
                                       bool isPublic )
    {
        var r = OpenWorkingFolder( monitor, workingFolder, warnOnly: false );
        if( r == null ) return null;

        var gitKey = new GitRepositoryKey( secretsStore, r.Value.OriginUrl, isPublic );
        return new GitRepository( gitKey, r.Value.Repository, workingFolder, displayPath );
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
            if( !git.GetReadCredentials( monitor, out var creds ) ) return null;
            try
            {
                Repository.Clone( git.OriginUrl.AbsoluteUri, workingFolder, new CloneOptions()
                {
                    FetchOptions =
                                    {
                                        CredentialsProvider = ( url, user, cred ) => creds
                                    },
                    Checkout = true
                } );
                var r = new Repository( workingFolder );
                var remote = r.Network.Remotes.FirstOrDefault( rem => GitRepositoryKey.IsEquivalentRepositoryUri( new Uri( rem.Url, UriKind.Absolute ), git.OriginUrl ) );
                if( remote == null || remote.Name != "origin" )
                {

                    monitor.Fatal( $"Existing '{workingFolder}' must have its 'origin' remote set to '{git.OriginUrl}'. This must be fixed manually." );
                    r.Dispose();
                    return null;
                }
                EnsureFirstCommit( monitor, r );
                return r;
            }
            catch( Exception ex )
            {
                monitor.Error( "Git clone failed. Leaving existing directory as-is.", ex );
                return null;
            }
        }
    }

    /// <summary>
    /// Tries to open an existing working folder. An "origin" remote must exist.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="workingFolder">The local working folder (above the .git folder).</param>
    /// <param name="warnOnly">True to emit only warnings on error, false to emit errors.</param>
    /// <returns>The LibGit2Sharp repository object and its "origin" Url or null on error.</returns>
    public static (Repository Repository, Uri OriginUrl)? OpenWorkingFolder( IActivityMonitor monitor,
                                                                             NormalizedPath workingFolder,
                                                                             bool warnOnly )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckArgument( !workingFolder.IsEmptyPath );
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
            var r = new Repository( workingFolder );
            var origin = r.Network.Remotes.FirstOrDefault( rem => rem.Name == "origin" );
            if( origin == null )
            {
                monitor.Log( errorLevel, $"""
                                          Existing '{workingFolder}' must have an 'origin' remote. Remotes are: '{r.Network.Remotes.Select( r => r.Name ).Concatenate( "', '" )}'.
                                          This must be fixed manually.
                                          """ );
                r.Dispose();
                return null;
            }
            
            if( !Uri.TryCreate( origin.Url, UriKind.Absolute, out var originUrl ) )
            {
                monitor.Log( errorLevel, $"""
                                          Existing '{workingFolder}' has its 'origin' that is not a valid absolute Uri: '{origin.Url}'.
                                          This must be fixed manually.
                                          """ );
                r.Dispose();
                return null;
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
            m.Info( $"Unitialized repository: automatically creating an initial commit." );
            var date = DateTimeOffset.Now;
            Signature author = r.Config.BuildSignature( date );
            var committer = new Signature( "CKli", "none", date );
            r.Commit( "Initial commit automatically created.", author, committer, new CommitOptions { AllowEmptyCommit = true } );
        }
    }

}
