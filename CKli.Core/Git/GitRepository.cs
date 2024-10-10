using CK.Core;
using LibGit2Sharp;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Core;


/// <summary>
/// Abstract base that adds useful methods to LibGit2Sharp <see cref="Repository"/>.
/// Base class of <see cref="SimpleGitRepository"/> and FileSystem's <see cref="GitRepository"/>.
/// </summary>
public class GitRepository : IGitHeadInfo, IDisposable
{
    static readonly StatusOptions _checkDirtyOptions = new StatusOptions() { IncludeIgnored = false };
    readonly GitRepositoryKey _repositoryKey;
    readonly Repository _git;
    readonly NormalizedPath _displayPath;
    readonly NormalizedPath _fullPhysicalPath;

    GitRepository( GitRepositoryKey repositoryKey,
                   Repository libRepository,
                   in NormalizedPath fullPath,
                   in NormalizedPath displayPath )
    {
        Throw.CheckNotNullArgument( repositoryKey );
        Throw.CheckNotNullArgument( libRepository );

        _repositoryKey = repositoryKey;
        _git = libRepository;
        _fullPhysicalPath = fullPath;
        _displayPath = displayPath;

        Throw.CheckArgument( _fullPhysicalPath == libRepository.Info.WorkingDirectory );
        Throw.CheckArgument( _fullPhysicalPath.EndsWith( _displayPath, strict: false ) );
    }

    /// <summary>
    /// Checks out a working folder if needed or checks that an existing one is
    /// bound to the <see cref="GitRepositoryKey.OriginUrl"/> 'origin' remote, ensuring
    /// that the specified branch name exists (and optionally checked out).
    /// <para>Returns a GitRepository object or null on error.</para>
    /// </summary>
    /// <param name="m">The monitor to use.</param>
    /// <param name="git">The Git key.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="subPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// (this can be the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
    /// </param>
    /// <param name="branchName">
    /// The initial branch name if cloning is done and the branch that must be
    /// checked out if <paramref name="checkOutBranchName"/> is true.
    /// This branch is always created as needed (just like <see cref="EnsureBranch"/> does).
    /// </param>
    /// <param name="checkOutBranchName">
    /// True to always check out the <paramref name="branchName"/>
    /// even if the repository already exists.
    /// </param>
    /// <returns>The SimpleGitRepository object or null on error.</returns>
    public static GitRepository? Ensure( IActivityMonitor m,
                                         GitRepositoryKey git,
                                         NormalizedPath workingFolder,
                                         NormalizedPath subPath,
                                         string branchName,
                                         bool checkOutBranchName )
    {
        var r = EnsureWorkingFolder( m, git, workingFolder, branchName );
        if( r == null ) return null;
        var g = new GitRepository( git, r, workingFolder, subPath );
        return CheckOutIfNeeded( m, branchName, checkOutBranchName, g );
    }

    /// <summary>
    /// Opens a working folder.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The key store to use.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="displayPath">
    /// The short path to display, relative to a well known root. It must not be empty.
    /// (this can be the <see cref="NormalizedPath.LastPart"/> of the <paramref name="workingFolder"/>.)
    /// </param>
    /// <param name="isPublic">Whether this repository is a public or private one.</param>
    /// <param name="branchName">
    /// An optional branch name that is created and checked out if <paramref name="checkOutBranchName"/> is true.
    /// </param>
    /// <param name="checkOutBranchName">
    /// True to always check out the <paramref name="branchName"/>.
    /// </param>
    /// <returns>The SimpleGitRepository object or null on error.</returns>
    public static GitRepository? Open( IActivityMonitor monitor,
                                       ISecretsStore secretsStore,
                                       NormalizedPath workingFolder,
                                       NormalizedPath displayPath,
                                       bool isPublic,
                                       string? branchName,
                                       bool checkOutBranchName )
    {
        Throw.CheckArgument( !checkOutBranchName || branchName != null );
        var r = OpenWorkingFolder( monitor, workingFolder, warnOnly: false, branchName );
        if( r == null ) return null;

        var gitKey = new GitRepositoryKey( secretsStore, r.Value.OriginUrl, isPublic );
        var g = new GitRepository( gitKey, r.Value.Repository, workingFolder, displayPath );
        return CheckOutIfNeeded( monitor, branchName, checkOutBranchName, g );
    }

    static GitRepository? CheckOutIfNeeded( IActivityMonitor monitor, string? branchName, bool checkOutBranchName, GitRepository g )
    {
        if( branchName != null
            && checkOutBranchName
            && branchName != g.CurrentBranchName
            && !g.Checkout( monitor, branchName ).Success )
        {
            g.Dispose();
            return null;
        }
        return g;
    }

    /// <summary>
    /// Disposes the <see cref="_git"/> member.
    /// </summary>
    public virtual void Dispose()
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
    /// Full physical path is the same as LibGit's <see cref="RepositoryInformation.WorkingDirectory"/>.
    /// </summary>
    public NormalizedPath FullPhysicalPath => _fullPhysicalPath;

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
    /// <param name="DisplayName">The Git folder name.</param>
    /// <param name="CurrentBranchName">The currently checked out branch.</param>
    /// <param name="IsDirty">Whether the working folder is dirty.</param>
    /// <param name="CommitAhead">
    /// The number of commit that are ahead of the origin.
    /// 0 mean that there a no commit ahead of origin.
    /// Null if there is no origin (the branch is not tacked).</param>
    public readonly record struct SimpleStatusInfo( NormalizedPath DisplayName, string CurrentBranchName, bool IsDirty, int? CommitAhead );

    /// <summary>
    /// Gets a <see cref="SimpleStatusInfo"/> for this repository.
    /// </summary>
    /// <returns>The simplified status info</returns>
    public SimpleStatusInfo GetSimpleStatusInfo()
    {
        return new SimpleStatusInfo()
        {
            DisplayName = DisplayPath,
            CommitAhead = _git.Head.TrackingDetails.AheadBy,
            CurrentBranchName = _git.Head.FriendlyName,
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
    /// <param name="m">The monitor to use.</param>
    /// <param name="branchName">The branch name. Must not be null or white space.</param>
    /// <returns>The Sha or null.</returns>
    public string? GetBranchSha( IActivityMonitor m, string branchName )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
        var b = GetBranch( m, branchName, false );
        return b?.Tip.Sha;
    }

    /// <summary>
    /// Gets whether the head can be amended: the current branch
    /// is not tracked or the current commit is ahead of the remote branch.
    /// </summary>
    public bool CanAmendCommit => (_git.Head.TrackingDetails.AheadBy ?? 1) > 0;

    Branch? GetBranch( IActivityMonitor m, string branchName, bool logErrorMissingLocalAndRemote )
    {
        return DoGetBranch( m, _git, branchName, logErrorMissingLocalAndRemote, DisplayPath );
    }

    static Branch? DoGetBranch( IActivityMonitor m, Repository r, string branchName, bool logErrorMissingLocalAndRemote, string repoDisplayName )
    {
        var b = r.Branches[branchName];
        if( b == null )
        {
            string remoteName = "origin/" + branchName;
            var remote = r.Branches[remoteName];
            if( remote == null )
            {
                var msg = $"Repository '{repoDisplayName}': Both local '{branchName}' and remote '{remoteName}' not found.";
                if( logErrorMissingLocalAndRemote ) m.Error( msg );
                else m.Warn( msg );
                return null;
            }
            m.Info( $"Creating local branch on remote '{remoteName}' in repository '{repoDisplayName}'." );
            b = r.Branches.Add( branchName, remote.Tip );
            b = r.Branches.Update( b, u => u.TrackedBranch = remote.CanonicalName );
        }
        return b;
    }

    /// <summary>
    /// Ensures that a local branch exists.
    /// If the branch is created, it will point at the same commit as the current <see cref="Head"/>.
    /// The branch is guaranteed to exist but the <see cref="CurrentBranchName"/> stays where it is.
    /// Use <see cref="Checkout(IActivityMonitor, string, bool, bool)"/> to change the current branch.
    /// </summary>
    /// <param name="m">The monitor to use.</param>
    /// <param name="branchName">The branch name.</param>
    public void EnsureBranch( IActivityMonitor m, string branchName, bool noWarnOnCreate = false )
    {
        DoEnsureBranch( m, _git, branchName, noWarnOnCreate, DisplayPath );
    }

    static Branch DoEnsureBranch( IActivityMonitor m, Repository r, string branchName, bool noWarnOnCreate, string repoDisplayName )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
        var b = DoGetBranch( m, r, branchName, logErrorMissingLocalAndRemote: false, repoDisplayName: repoDisplayName );
        if( b == null )
        {
            m.Log( noWarnOnCreate ? LogLevel.Info : LogLevel.Warn, $"Branch '{branchName}' does not exist. Creating local branch." ); ;
            b = r.CreateBranch( branchName );
        }
        return b;
    }

    /// <summary>
    /// Fetches 'origin' (or all remotes) branches into this repository.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>
    /// Success is true on success, false on error.
    /// </returns>
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
    /// Pull-Merge the current commit from the remote. Any merge conflict is an error with <see cref="MergeFileFavor.Normal"/> and this is the
    /// safest mode. Choosing one of other flavors will not trigger an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="mergeFileFavor">How merge must be done.</param>
    /// <param name="fastForwardStrategy">The fast forward strategy to apply.</param>
    /// <returns>A MergeResult: both <see cref="MergeResult.Error"/> and <see cref="MergeResult.ErrorConflicts"/> are failures.</returns>
    public MergeResult Pull( IActivityMonitor monitor, MergeFileFavor mergeFileFavor = MergeFileFavor.Normal, FastForwardStrategy fastForwardStrategy = FastForwardStrategy.Default )
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
    /// Checks out a branch, calling <see cref="FetchBranches(IActivityMonitor, bool)"/>
    /// and <see cref="Pull(IActivityMonitor, MergeFileFavor, FastForwardStrategy)"/> by default.
    /// There must not be any uncommitted changes on the current head.
    /// The branch must exist locally or on the 'origin' remote.
    /// If the branch exists only in the "origin" remote, a local branch is automatically
    /// created that tracks the remote one.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="branchName">The local name of the branch.</param>
    /// <param name="skipFetchBranches">True to not call <see cref="FetchBranches(IActivityMonitor, bool)"/>.</param>
    /// <param name="skipPullMerge">True to not "pull merge" from the remote after having checked out the branch.</param>
    /// <returns>True on success, false on error (such as merge conflicts).
    /// </returns>
    public bool Checkout( IActivityMonitor monitor, string branchName, bool skipFetchBranches = false, bool skipPullMerge = false )
    {
        using( monitor.OpenInfo( $"Checking out branch '{branchName}' in '{DisplayPath}'." ) )
        {
            if( !skipFetchBranches && !FetchBranches( monitor ) ) return false;
            try
            {
                Branch? b = GetBranch( monitor, branchName, logErrorMissingLocalAndRemote: true );
                if( b == null ) return false;
                if( b.IsCurrentRepositoryHead )
                {
                    monitor.Trace( $"Already on {branchName}." );
                }
                else
                {
                    if( !CheckCleanCommit( monitor ) ) return false;
                    monitor.Info( $"Checking out {branchName} (leaving {CurrentBranchName})." );
                    Commands.Checkout( _git, b );
                }
                if( skipPullMerge ) return false;

                return Pull( monitor, MergeFileFavor.Normal ) is not MergeResult.Error or MergeResult.ErrorConflicts;
            }
            catch( Exception ex )
            {
                monitor.Fatal( "Unexpected error. Manual fix should be required.", ex );
                return (false, true);
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
            return DoCommit( monitor, commitMessage, DateTimeOffset.Now, false );
        }
    }

    /// <summary>
    /// Amends the current commit, optionally changing its message and/or its date.
    /// <see cref="CanAmendCommit"/> must be true otherwise an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    /// <param name="m">The monitor to use.</param>
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
    public CommitResult AmendCommit( IActivityMonitor m,
                                         Func<string, string>? editMessage = null,
                                         Func<DateTimeOffset, DateTimeOffset?>? editDate = null,
                                         bool skipIfNothingToCommit = true )
    {
        if( !CanAmendCommit ) throw new InvalidOperationException( nameof( CanAmendCommit ) );
        using( m.OpenInfo( $"Amending Commit in '{DisplayPath}' (branch '{CurrentBranchName}')." ) )
        {
            var message = _git.Head.Tip.Message;
            if( editMessage != null ) message = editMessage( message );
            if( String.IsNullOrWhiteSpace( message ) )
            {
                m.CloseGroup( "Canceled by empty message." );
                return CommitResult.Error;
            }
            DateTimeOffset initialDate = _git.Head.Tip.Committer.When;
            DateTimeOffset? date = initialDate;
            if( editDate != null ) date = editDate( date.Value );
            if( date == null )
            {
                m.CloseGroup( "Canceled by null date." );
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
                        m.Trace( "Adjusted commit date to the next second." );
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
                        m.Info( "Updating message and date." );
                    }
                    else if( dateUpdate )
                    {
                        m.Info( "Updating commit date." );
                    }
                    else if( messageUpdate )
                    {
                        m.Info( "Only updating message." );
                    }
                    else skipIfNothingToCommit = true;
                }
                if( skipIfNothingToCommit )
                {
                    m.CloseGroup( "Working folder is up-to-date." );
                    return CommitResult.NoChanges;
                }
            }
            return DoCommit( m, message, date.Value, true );
        }
    }

    CommitResult DoCommit( IActivityMonitor m, string commitMessage, DateTimeOffset date, bool amendPreviousCommit )
    {
        using var grp = m.OpenTrace( "Committing changes..." );
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
                grp.ConcludeWith( () => $"Committed changes." );
                return amendPreviousCommit ? CommitResult.Amended : CommitResult.Commited;
            }
            catch( EmptyCommitException )
            {
                if( !amendPreviousCommit ) throw;
                Throw.DebugAssert( "This check on merge commit is already done by LibGit2Sharp.", _git.Head.Tip.Parents.Count() == 1 );
                grp.ConcludeWith( () => "No actual changes. Reseting branch to parent commit." );
                _git.Reset( ResetMode.Hard, _git.Head.Tip.Parents.Single() );
                Throw.DebugAssert( options.AmendPreviousCommit = true );
                string sha = _git.Head.Tip.Sha;
                _git.Commit( commitMessage, author, committer, options );
                return sha == _git.Head.Tip.Sha ? CommitResult.NoChanges : CommitResult.Amended;
            }
        }
        catch( Exception ex )
        {
            m.Error( ex );
            return CommitResult.Error;
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
                    monitor.Error( $"Unable to find branch '{branchName}'." );
                    return false;
                }
                bool created = false;
                if( !b.IsTracking )
                {
                    monitor.Warn( $"Branch '{branchName}' does not exist on the remote. Creating the remote branch on 'origin'." );
                    _git.Branches.Update( b, u => { u.Remote = "origin"; u.UpstreamBranch = b.CanonicalName; } );
                    created = true;
                }
                var options = new PushOptions()
                {
                    CredentialsProvider = ( url, user, types ) => creds,
                    OnPushStatusError = ( e ) =>
                    {
                        throw new InvalidOperationException( $"Error while pushing ref {e.Reference} => {e.Message}" );
                    }
                };
                if( created || (b.TrackingDetails.AheadBy ?? 1) > 0 )
                {
                    _git.Network.Push( b, options );
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
    /// Extension point: called whenever a branch that has not been seen yet
    /// has been checked out.
    /// </summary>
    /// <param name="m">The monitor to use.</param>
    protected virtual void OnNewCurrentBranch( IActivityMonitor m )
    {
    }

    /// <summary>
    /// Checks out a working folder if needed or checks that an existing one is
    /// bound to the <see cref="GitRepositoryKey.OriginUrl"/> 'origin' remote.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretStore">The secret key store.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="url">The url of the remote.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    /// <param name="branchName">
    /// The initial branch name if cloning is done.
    /// This branch is created if needed (just like <see cref="EnsureBranch"/> does).
    /// It is checked out only if the repository has been created.
    /// </param>
    /// <returns>The LibGit2Sharp repository object or null on error.</returns>
    public static bool EnsureWorkingFolder( IActivityMonitor monitor,
                                            ISecretsStore secretStore,
                                            NormalizedPath workingFolder,
                                            Uri url,
                                            bool isPublic,
                                            string? branchName )
    {
        var r = EnsureWorkingFolder( monitor,
                                     new GitRepositoryKey( secretStore, url, isPublic ),
                                     workingFolder,
                                     branchName );
        if( r == null ) return false;
        r.Dispose();
        return true;
    }

    /// <summary>
    /// Checks out a working folder if needed or checks that an existing one is
    /// bound to the <see cref="GitRepositoryKey.OriginUrl"/> 'origin' remote.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="git">The Git key.</param>
    /// <param name="workingFolder">The local working folder.</param>
    /// <param name="branchName">
    /// The initial branch name if cloning is done.
    /// This branch is created if needed (just like <see cref="EnsureBranch"/> does).
    /// It is checked out only if the repository has been created.
    /// </param>
    /// <returns>The LibGit2Sharp repository object or null on error.</returns>
    public static Repository? EnsureWorkingFolder( IActivityMonitor monitor,
                                                   GitRepositoryKey git,
                                                   NormalizedPath workingFolder,
                                                   string? branchName = null )
    {
        using( monitor.OpenTrace( $"Ensuring working folder '{workingFolder}' on '{git.OriginUrl}'." ) )
        {
            try
            {
                var gitFolderPath = workingFolder.AppendPart( ".git" );
                bool repoCreated = false;
                if( !Directory.Exists( gitFolderPath ) )
                {
                    using( monitor.OpenTrace( $"The folder '{gitFolderPath}' does not exist." ) )
                    {
                        using( monitor.OpenInfo( $"Cloning '{workingFolder}' from '{git.OriginUrl}' on {branchName}." ) )
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
                                repoCreated = true;
                            }
                            catch( Exception ex )
                            {
                                monitor.Error( "Git clone failed. Leaving existing directory as-is.", ex );
                                return null;
                            }
                        }
                    }
                }
                Repository r;
                using( monitor.OpenTrace( "Checking the validity of the git repository." ) )
                {
                    if( !Repository.IsValid( gitFolderPath ) )
                    {
                        monitor.Fatal( $"Git folder '{gitFolderPath}' exists but is not a valid Repository." );
                        return null;
                    }
                    r = new Repository( workingFolder );
                    var remote = r.Network.Remotes.FirstOrDefault( rem => GitRepositoryKey.IsEquivalentRepositoryUri( new Uri( rem.Url, UriKind.Absolute ), git.OriginUrl ) );
                    if( remote == null || remote.Name != "origin" )
                    {

                        monitor.Fatal( $"Existing '{workingFolder}' must have its 'origin' remote set to '{git.OriginUrl}'. This must be fixed manually." );
                        r.Dispose();
                        return null;
                    }
                    EnsureFirstCommit( monitor, r );
                }
                if( r.Head?.FriendlyName != branchName && branchName != null )
                {
                    Branch branch = DoEnsureBranch( monitor, r, branchName, false, workingFolder );
                    if( repoCreated ) Commands.Checkout( r, branch );
                }
                monitor.CloseGroup( "Repository is checked out." );
                return r;
            }
            catch( Exception ex )
            {
                monitor.Fatal( $"Failed to ensure Git '{workingFolder}'.", ex );
                return null;
            }
        }
    }

    /// <summary>
    /// Calls <see cref="EnsureWorkingFolder(IActivityMonitor, GitRepositoryKey, NormalizedPath, bool, string?)"/>
    /// on multiple repositories at once.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretStore">The key store.</param>
    /// <param name="stackRoot">The root of the <paramref name="layout"/>.</param>
    /// <param name="layout">The set of repositories to ensure.</param>
    /// <param name="isPublic">Whether the repositories are public or not.</param>
    /// <param name="branchName">
    /// The initial branch name if cloning is done.
    /// This branch is created if needed (just like <see cref="EnsureBranch"/> does).
    /// It is checked out only if the repository has been created.
    /// </param>
    /// <returns>True on success, false is at least one repository failed.</returns>
    public static bool EnsureWorkingFolders( IActivityMonitor monitor,
                                             ISecretsStore secretStore,
                                             NormalizedPath stackRoot,
                                             IReadOnlyList<(NormalizedPath SubPath, Uri Url)> layout,
                                             bool isPublic,
                                             string? branchName = null )
    {
        bool success = true;
        using( monitor.OpenInfo( $"Cloning {layout.Count} repositories in {stackRoot}." ) )
        {
            foreach( var (subPath, url) in layout )
            {
                success &= EnsureWorkingFolder( monitor, secretStore, stackRoot.Combine( subPath ), url, isPublic, branchName );
            }
        }
        return success;
    }

    /// <summary>
    /// Tries to open an existing working folder. An "origin" remote must exist.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="workingFolder">The local working folder (above the .git folder).</param>
    /// <param name="warnOnly">True to emit only warnings on error, false to emit errors.</param>
    /// <param name="branchName">
    /// An optional branch name that is created if it doesn't exist but is not checked out.
    /// <see cref="EnsureBranch(IActivityMonitor, string, bool)"/>.
    /// </param>
    /// <returns>The LibGit2Sharp repository object and its "origin" Url or null on error.</returns>
    public static (Repository Repository, Uri OriginUrl)? OpenWorkingFolder( IActivityMonitor monitor,
                                                                             NormalizedPath workingFolder,
                                                                             bool warnOnly,
                                                                             string? branchName = null )
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
                monitor.Log( errorLevel, $"Existing '{workingFolder}' must have an 'origin' remote. Remotes are: '{r.Network.Remotes.Select( r => r.Name ).Concatenate( "', '" )}'. This must be fixed manually." );
                r.Dispose();
                return null;
            }
            if( !Uri.TryCreate( origin.Url, UriKind.Absolute, out var originUrl ) )
            {
                monitor.Log( errorLevel, $"Existing '{workingFolder}' has its 'origin' that is not a valid absolute Uri: '{origin.Url}'. This must be fixed manually." );
                r.Dispose();
                return null;
            }
            EnsureFirstCommit( monitor, r );
            if( branchName != null && r.Head?.FriendlyName != branchName )
            {
                DoEnsureBranch( monitor, r, branchName, false, workingFolder );
            }
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
