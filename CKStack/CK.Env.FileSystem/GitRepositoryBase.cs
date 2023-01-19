using CK.Core;
using CK.SimpleKeyVault;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Abstract base that adds useful methods to LibGit2Sharp <see cref="Repository"/>.
    /// Base class of <see cref="SimpleGitRepository"/> and FileSystem's <see cref="GitRepository"/>.
    /// </summary>
    public abstract class GitRepositoryBase : IGitHeadInfo, IDisposable
    {
        internal static readonly StatusOptions _checkDirtyOptions = new StatusOptions() { IncludeIgnored = false };

        /// <summary>
        /// Initializes a new <see cref="GitRepositoryBase"/>.
        /// </summary>
        /// <param name="repositoryKey">The repository key.</param>
        /// <param name="libRepository">The actual LibGit2Sharp repository instance.</param>
        /// <param name="fullPath">The working folder.</param>
        /// <param name="displayPath">See <see cref="DisplayPath"/>. Can not be empty.</param>
        protected GitRepositoryBase( GitRepositoryKey repositoryKey,
                                     Repository libRepository,
                                     in NormalizedPath fullPath,
                                     in NormalizedPath displayPath )
        {
            Throw.CheckNotNullArgument( repositoryKey );
            Throw.CheckNotNullArgument( libRepository );
            RepositoryKey = repositoryKey;
            Git = libRepository;
            FullPhysicalPath = fullPath;
            DisplayPath = displayPath;

            Throw.CheckArgument( FullPhysicalPath == libRepository.Info.WorkingDirectory );
            Throw.CheckArgument( FullPhysicalPath.EndsWith( DisplayPath, strict: false ) );
        }

        /// <summary>
        /// The repository key.
        /// </summary>
        protected readonly GitRepositoryKey RepositoryKey;

        /// <summary>
        /// The LibGit2Sharp repository itself.
        /// </summary>
        protected readonly Repository Git;

        /// <summary>
        /// Disposes the <see cref="Git"/> member.
        /// </summary>
        public virtual void Dispose()
        {
            Git.Dispose();
        }

        /// <summary>
        /// Gets whether the Git repository is public or private.
        /// </summary>
        public bool IsPublic => RepositoryKey.IsPublic;

        /// <summary>
        /// Gets the remote origin url.
        /// </summary>
        public Uri OriginUrl => RepositoryKey.OriginUrl;

        /// <summary>
        /// The short path to display.
        /// </summary>
        public NormalizedPath DisplayPath { get; }

        /// <summary>
        /// Full physical path is the same as LibGit's <see cref="RepositoryInformation.WorkingDirectory"/>.
        /// </summary>
        public NormalizedPath FullPhysicalPath { get; }

        /// <summary>
        /// Gets the current branch name (name of the repository's HEAD).
        /// </summary>
        public string CurrentBranchName => Git.Head.FriendlyName;

        /// <summary>
        /// Gets the git provider kind.
        /// </summary>
        public KnownGitProvider KnownGitProvider => RepositoryKey.KnownGitProvider;

        /// <summary>
        /// Gets the head information.
        /// </summary>
        public IGitHeadInfo Head => this;

        string IGitHeadInfo.CommitSha => Git.Head.Tip.Sha;

        string IGitHeadInfo.Message => Git.Head.Tip.Message;

        DateTimeOffset IGitHeadInfo.CommitDate => Git.Head.Tip.Committer.When;

        int? IGitHeadInfo.AheadOriginCommitCount => Git.Head.TrackingDetails.AheadBy;

        string? IGitHeadInfo.GetSha( string? path )
        {
            if( string.IsNullOrEmpty( path ) ) return Git.Head.Tip.Sha;
            var e = Git.Head.Tip.Tree[path];
            return e?.Target.Sha;
        }

        /// <summary>
        /// Captures minimal status information.
        /// </summary>
        public struct SimpleStatusInfo
        {
            /// <summary>
            /// The Git folder name.
            /// </summary>
            public NormalizedPath DisplayName;

            /// <summary>
            /// The currently checked out branch.
            /// </summary>
            public string CurrentBranchName;

            /// <summary>
            /// Whether the WorkingFolder is dirty.
            /// </summary>
            public bool IsDirty;

            /// <summary>
            /// The number of commit that are ahead of the origin.
            /// 0 mean that there a no commit ahead of origin.
            /// Null if there is no origin (the branch is not tacked).
            /// </summary>
            public int? CommitAhead;

            /// <summary>
            /// The number of plugins that are associated to this branch.
            /// Null if a plugin initialization occurred: this folder is on error.
            /// </summary>
            public int? PluginCount;
        }

        /// <summary>
        /// Gets a <see cref="SimpleStatusInfo"/> for this repository.
        /// The <see cref="SimpleStatusInfo.PluginCount"/> is let to null at this level.
        /// </summary>
        /// <returns>The simplified status info</returns>
        public SimpleStatusInfo GetSimpleStatusInfo()
        {
            return new SimpleStatusInfo()
            {
                DisplayName = DisplayPath,
                CommitAhead = Git.Head.TrackingDetails.AheadBy,
                CurrentBranchName = Git.Head.FriendlyName,
                IsDirty = Git.RetrieveStatus( _checkDirtyOptions ).IsDirty,
            };
        }

        /// <summary>
        /// Gets whether the head can be amended: the current branch
        /// is not tracked or the current commit is ahead of the remote branch.
        /// </summary>
        public bool CanAmendCommit => (Git.Head.TrackingDetails.AheadBy ?? 1) > 0;

        /// <summary>
        /// Checks that the current head is a clean commit (working directory is clean and no staging files exists).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True if the current head is clean, false otherwise.</returns>
        public bool CheckCleanCommit( IActivityMonitor m )
        {
            if( Git.RetrieveStatus( _checkDirtyOptions ).IsDirty )
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

        Branch? GetBranch( IActivityMonitor m, string branchName, bool logErrorMissingLocalAndRemote )
        {
            return DoGetBranch( m, Git, branchName, logErrorMissingLocalAndRemote, DisplayPath );
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
            DoEnsureBranch( m, Git, branchName, noWarnOnCreate, DisplayPath );
        }

        /// <summary>
        /// Ensure that a branch exists. If the branch is created, it will point at the same commit as the current <see cref="Head"/>.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="r">The repository.</param>
        /// <param name="branchName">The name of the branch.</param>
        /// <param name="noWarnOnCreate">Log as warning if the branch is created.</param>
        /// <param name="repoDisplayName">Name of the repository displayed in the logs.</param>
        /// <returns>The Branch.</returns>
        static Branch DoEnsureBranch( IActivityMonitor m, Repository r, string branchName, bool noWarnOnCreate, string repoDisplayName )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
            var b = DoGetBranch( m, r, branchName, logErrorMissingLocalAndRemote: false, repoDisplayName: repoDisplayName );
            if( b == null )
            {
                m.Log( noWarnOnCreate ? Core.LogLevel.Info : Core.LogLevel.Warn, $"Branch '{branchName}' does not exist. Creating local branch." ); ;
                b = r.CreateBranch( branchName );
            }
            return b;
        }

        /// <summary>
        /// Fetches 'origin' (or all remotes) branches into this repository.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>
        /// Success is true on success, false on error.
        /// </returns>
        [CommandMethod]
        public bool FetchBranches( IActivityMonitor m, bool originOnly = true )
        {
            using( m.OpenInfo( $"Fetching {(originOnly ? "origin" : "all remotes")} in repository '{DisplayPath}'." ) )
            {
                try
                {
                    foreach( Remote remote in Git.Network.Remotes.Where( r => !originOnly || r.Name == "origin" ) )
                    {
                        m.Info( $"Fetching remote '{remote.Name}'." );
                        IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select( x => x.Specification ).ToArray();
                        Commands.Fetch( Git, remote.Name, refSpecs, new FetchOptions()
                        {
                            CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( m ),
                            TagFetchMode = TagFetchMode.All
                        }, $"Fetching remote '{remote.Name}'." );
                    }
                    return true;
                }
                catch( Exception ex )
                {
                    using( m.OpenFatal( "The following error need manual fix:" ) )
                    {
                        m.Fatal( ex );
                    }
                    return false;
                }
            }
        }

        /// <summary>
        /// Pulls current branch by merging changes from remote 'origin' branch into this repository.
        /// The current head must be clean.
        /// Note that this is not a [CommandMethod]: Pull command is implemented by Solution driver
        /// so that potential reloading solution is handled.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        public (bool Success, bool ReloadNeeded) Pull( IActivityMonitor m, MergeFileFavor mergeFileFavor )
        {
            using( m.OpenInfo( $"Pulling branch '{CurrentBranchName}' in '{DisplayPath}'." ) )
            {
                if( !FetchBranches( m )
                    || !CheckCleanCommit( m ) )
                {
                    return (false, false);
                }

                EnsureBranch( m, CurrentBranchName );

                try
                {
                    return DoPull( m, mergeFileFavor );
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return (false, true);
                }
            }
        }

        /// <summary>
        /// Checks out a branch: calls <see cref="FetchAll"/> and pulls remote 'origin' branch changes.
        /// There must not be any uncommitted changes on the current head.
        /// The branch must exist locally or on the 'origin' remote.
        /// If the branch exists only in the "origin" remote, a local branch is automatically
        /// created that tracks the remote one.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="branchName">The local name of the branch.</param>
        /// <param name="skipFetchBranches">True to not call <see cref="FetchBranches(IActivityMonitor, bool)"/>.</param>
        /// <param name="skipPullMerge">True to not "pull merge" from the remote after having checked out the branch.</param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        public (bool Success, bool ReloadNeeded) Checkout( IActivityMonitor m, string branchName, bool skipFetchBranches = false, bool skipPullMerge = false )
        {
            using( m.OpenInfo( $"Checking out branch '{branchName}' in '{DisplayPath}'." ) )
            {
                if( !skipFetchBranches && !FetchBranches( m ) ) return (false, false);
                try
                {
                    bool reloadNeeded = false;
                    Branch? b = GetBranch( m, branchName, logErrorMissingLocalAndRemote: true );
                    if( b == null ) return (false, false);
                    if( b.IsCurrentRepositoryHead )
                    {
                        m.Trace( $"Already on {branchName}." );
                    }
                    else
                    {
                        if( !CheckCleanCommit( m ) ) return (false, false);
                        var savedCurrent = CurrentBranchName;
                        m.Info( $"Checking out {branchName} (leaving {CurrentBranchName})." );
                        Commands.Checkout( Git, b );
                        try
                        {
                            OnNewCurrentBranch( m );
                            reloadNeeded = true;
                        }
                        catch( Exception ex )
                        {
                            m.Error( $"Error while calling OnNewCurrentBranch. Restoring '{savedCurrent}' checked out branch.", ex );
                            Commands.Checkout( Git, Git.Branches[savedCurrent] );
                            return (false, false);
                        }
                    }
                    if( skipPullMerge ) return (true, reloadNeeded);

                    (bool Success, bool ReloadNeeded) = DoPull( m, MergeFileFavor.Theirs );
                    return (Success, reloadNeeded || ReloadNeeded);
                }
                catch( Exception ex )
                {
                    m.Fatal( "Unexpected error. Manual fix should be required.", ex );
                    return (false, true);
                }
            }
        }

        (bool Success, bool ReloadNeeded) DoPull( IActivityMonitor m, MergeFileFavor mergeFileFavor )
        {
            var merger = Git.Config.BuildSignature( DateTimeOffset.Now ) ?? new Signature( "CKli", "none", DateTimeOffset.Now );
            if( Git.Branches.Count() == 1 )
            {
                // There's only one branch.
                var unique = Git.Branches.Single();
                if( unique.TrackedBranch?.Tip == null )
                {
                    Debug.Assert( !Git.Branches.Single().IsRemote );
                    m.Warn( $"The remote repository is not initialized and have 0 commits. We can't pull since there is only 1 local branch '{unique.FriendlyName}'." );
                    if( unique.FriendlyName != IWorldName.MasterName )
                    {
                        m.Warn( $"The single (main) branch should be '{IWorldName.MasterName}', not '{unique.FriendlyName}'." );
                    }
                    return (true, false);
                }
            }
            if( Git.Head.TrackedBranch == null )
            {
                m.Warn( $"There is no tracking branch for the current branch. Skip pulling from the remote." );
                return (true, false);
            }
            var result = Commands.Pull( Git, merger, new PullOptions
            {
                FetchOptions = new FetchOptions
                {
                    TagFetchMode = TagFetchMode.All,
                    CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( m )
                },
                MergeOptions = new MergeOptions
                {
                    MergeFileFavor = mergeFileFavor,
                    CommitOnSuccess = true,
                    FailOnConflict = true,
                    FastForwardStrategy = FastForwardStrategy.Default,
                    SkipReuc = true
                }
            } );
            if( result.Status == MergeStatus.Conflicts )
            {
                m.Error( "Merge conflicts occurred. Unable to merge changes from the remote." );
                return (false, false);
            }
            return (true, result.Status != MergeStatus.UpToDate);
        }

        /// <summary>
        /// Commits any pending changes.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="commitMessage">
        /// Required commit message.
        /// This is ignored when <paramref name="amendIfPossible"/> and <see cref="CanAmendCommit"/> are both true.
        /// </param>
        /// <param name="amendIfPossible">
        /// True to call <see cref="AmendCommit"/> if <see cref="CanAmendCommit"/>. is true.
        /// </param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public CommittingResult Commit( IActivityMonitor m, string commitMessage, CommitBehavior commitBehavior = CommitBehavior.CreateNewCommit )
        {
            if( commitBehavior != CommitBehavior.CreateNewCommit && CanAmendCommit )
            {
                Func<string, string>? modified = null;
                switch( commitBehavior )
                {
                    case CommitBehavior.CreateNewCommit:
                        throw new InvalidOperationException();
                    case CommitBehavior.AmendIfPossibleAndKeepPreviousMessage:
                        modified = p => p;
                        break;
                    case CommitBehavior.AmendIfPossibleAndAppendPreviousMessage:
                        if( string.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
                        modified = p => $"{commitMessage}(...)\r\n{p}";
                        break;
                    case CommitBehavior.AmendIfPossibleAndPrependPreviousMessage:
                        if( string.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
                        modified = p => $"{p} (...)\r\n{commitMessage}";
                        break;
                    case CommitBehavior.AmendIfPossibleAndOverwritePreviousMessage:
                        if( string.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
                        modified = p => commitMessage;
                        break;
                    default:
                        throw new ArgumentException();
                }
                return AmendCommit( m, modified );
            }
            if( string.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
            using( m.OpenInfo( $"Committing changes in '{DisplayPath}' (branch '{CurrentBranchName}')." ) )
            {
                Commands.Stage( Git, "*" );
                var s = Git.RetrieveStatus( _checkDirtyOptions );
                if( !s.IsDirty )
                {
                    m.CloseGroup( "Working folder is up-to-date." );
                    return CommittingResult.NoChanges;
                }
                return DoCommit( m, commitMessage, DateTimeOffset.Now, false );
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
        public CommittingResult AmendCommit( IActivityMonitor m,
                                             Func<string, string>? editMessage = null,
                                             Func<DateTimeOffset, DateTimeOffset?>? editDate = null,
                                             bool skipIfNothingToCommit = true )
        {
            if( !CanAmendCommit ) throw new InvalidOperationException( nameof( CanAmendCommit ) );
            using( m.OpenInfo( $"Amending Commit in '{DisplayPath}' (branch '{CurrentBranchName}')." ) )
            {
                var message = Git.Head.Tip.Message;
                if( editMessage != null ) message = editMessage( message );
                if( String.IsNullOrWhiteSpace( message ) )
                {
                    m.CloseGroup( "Canceled by empty message." );
                    return CommittingResult.Error;
                }
                DateTimeOffset initialDate = Git.Head.Tip.Committer.When;
                DateTimeOffset? date = initialDate;
                if( editDate != null ) date = editDate( date.Value );
                if( date == null )
                {
                    m.CloseGroup( "Canceled by null date." );
                    return CommittingResult.Error;
                }
                Commands.Stage( Git, "*" );
                var s = Git.RetrieveStatus( _checkDirtyOptions );
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
                        bool messageUpdate = message != Git.Head.Tip.Message;
                        bool dateUpdate = date.Value != Git.Head.Tip.Committer.When;
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
                        return CommittingResult.NoChanges;
                    }
                }
                return DoCommit( m, message, date.Value, true );
            }
        }

        /// <returns>False on error. True otherwise.</returns>
        CommittingResult DoCommit( IActivityMonitor m, string commitMessage, DateTimeOffset date, bool amendPreviousCommit )
        {
            using var grp = m.OpenTrace( "Committing changes..." );
            try
            {
                Signature? author = amendPreviousCommit ? Git.Head.Tip.Author : Git.Config.BuildSignature( date );
                // Let AllowEmptyCommit even when amending: this avoids creating an empty commit.
                // If we are not amending, this is an error and we let the EmptyCommitException pops.
                var options = new CommitOptions { AmendPreviousCommit = amendPreviousCommit };
                var committer = new Signature( "CKli", "none", date );
                try
                {
                    Commit commit = Git.Commit( commitMessage, author ?? committer, committer, options );
                    grp.ConcludeWith( () => $"Committed changes." );
                    return amendPreviousCommit ? CommittingResult.Amended : CommittingResult.Commited;
                }
                catch( EmptyCommitException )
                {
                    if( !amendPreviousCommit ) throw;
                    Debug.Assert( Git.Head.Tip.Parents.Count() == 1, "This check on merge commit is already done by LibGit2Sharp." );
                    grp.ConcludeWith( () => "No actual changes. Reseting branch to parent commit." );
                    Git.Reset( ResetMode.Hard, Git.Head.Tip.Parents.Single() );
                    Debug.Assert( options.AmendPreviousCommit = true );
                    string sha = Git.Head.Tip.Sha;
                    Git.Commit( commitMessage, author, committer, options );
                    return sha == Git.Head.Tip.Sha ? CommittingResult.NoChanges : CommittingResult.Amended;
                }
            }
            catch( Exception ex )
            {
                m.Error( ex );
                return CommittingResult.Error;
            }
        }

        /// <summary>
        /// Gets whether <see cref="Push(IActivityMonitor)"/> can be called:
        /// the current branch is tracked and is ahead of the remote branch.
        /// </summary>
        /// <returns></returns>
        public bool CanPush => RepositoryKey.WritePATKeyName == null || RepositoryKey.SecretKeyStore.IsSecretKeyAvailable( RepositoryKey.WritePATKeyName ) == true
                               && (Git.Head.TrackingDetails.AheadBy ?? 0) > 0;

        /// <summary>
        /// Pushes changes from the current branch to the origin.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Push( IActivityMonitor m ) => Push( m, CurrentBranchName );

        /// <summary>
        /// Pushes changes from a branch to the origin.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Local branch name.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Push( IActivityMonitor m, string branchName )
        {
            Throw.CheckNotNullArgument( branchName );
            using( m.OpenInfo( $"Pushing '{DisplayPath}' (branch '{branchName}') to origin." ) )
            {
                try
                {
                    var b = Git.Branches[branchName];
                    if( b == null )
                    {
                        m.Error( $"Unable to find branch '{branchName}'." );
                        return false;
                    }
                    bool created = false;
                    if( !b.IsTracking )
                    {
                        m.Warn( $"Branch '{branchName}' does not exist on the remote. Creating the remote branch on 'origin'." );
                        Git.Branches.Update( b, u => { u.Remote = "origin"; u.UpstreamBranch = b.CanonicalName; } );
                        created = true;
                    }
                    var options = new PushOptions()
                    {
                        CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( m ),
                        OnPushStatusError = ( e ) =>
                        {
                            throw new InvalidOperationException( $"Error while pushing ref {e.Reference} => {e.Message}" );
                        }
                    };
                    if( created || (b.TrackingDetails.AheadBy ?? 1) > 0 )
                    {
                        Git.Network.Push( b, options );
                    }
                    else
                    {
                        m.CloseGroup( "Remote branch is on the same commit. Push skipped." );
                    }
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
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
        /// <param name="keyStore">The secret key store.</param>
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
                                                SecretKeyStore keyStore,
                                                NormalizedPath workingFolder,
                                                Uri url,
                                                bool isPublic,
                                                string? branchName )
        {
            var r = EnsureWorkingFolder( monitor,
                                         new GitRepositoryKey( keyStore, url, isPublic ),
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
                                try
                                {
                                    Repository.Clone( git.OriginUrl.AbsoluteUri, workingFolder, new CloneOptions()
                                    {
                                        CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( monitor, git ),
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
        /// <param name="keyStore">The key store.</param>
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
                                                 SecretKeyStore keyStore,
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
                    success &= EnsureWorkingFolder( monitor, keyStore, stackRoot.Combine( subPath ), url, isPublic, branchName );
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
                var errorLevel = warnOnly ? Core.LogLevel.Warn : Core.LogLevel.Error;
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

        /// <summary>
        /// Credentials is read from the <see cref="GitRepositoryKey.SecretKeyStore"/>.
        /// This cannot be implemented by GitRepositoryKey since LibGit2Sharp is not
        /// a dependency of CK.Env.Sys. 
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="git">The repository key.</param>
        /// <returns>The Credentials object that is null or a <see cref="UsernamePasswordCredentials"/>.</returns>
        internal static Credentials? PATCredentialsHandler( IActivityMonitor m, GitRepositoryKey git )
        {
            string? pat = git.ReadPATKeyName != null
                            ? git.SecretKeyStore.GetSecretKey( m, git.ReadPATKeyName, !git.IsPublic )
                            : null;
            return pat != null
                    ? new UsernamePasswordCredentials() { Username = "CKli", Password = pat }
                    : null;
        }

        /// <summary>
        /// Binds this <see cref="RepositoryKey"/> to the static <see cref="PATCredentialsHandler(IActivityMonitor, GitRepositoryKey)"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The Credentials object that is null or a <see cref="UsernamePasswordCredentials"/>.</returns>
        protected Credentials? PATCredentialsHandler( IActivityMonitor m ) => PATCredentialsHandler( m, RepositoryKey );
    }
}
