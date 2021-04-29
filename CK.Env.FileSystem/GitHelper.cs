using CK.Core;
using CK.Text;
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
    /// </summary>
    public abstract class GitHelper : IGitHeadInfo, IDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="GitHelper"/>.
        /// </summary>
        /// <param name="repositoryKey">The repository key.</param>
        /// <param name="libRepository">The actual LibGit2Sharp repository instance.</param>
        /// <param name="fullPath">The working folder.</param>
        /// <param name="subPath">See <see cref="SubPath"/>. Can not be empty.</param>
        protected GitHelper(
            GitRepositoryKey repositoryKey,
            Repository libRepository,
            NormalizedPath fullPath,
            NormalizedPath subPath )
        {
            RepositoryKey = repositoryKey ?? throw new ArgumentNullException( nameof( repositoryKey ) );
            Git = libRepository ?? throw new ArgumentNullException( nameof( libRepository ) );
            FullPhysicalPath = fullPath;
            SubPath = subPath;

            if( FullPhysicalPath != libRepository.Info.WorkingDirectory )
            {
                throw new ArgumentException( "Path mismatch.", nameof( fullPath ) );
            }
            if( !FullPhysicalPath.EndsWith( SubPath ) )
            {
                throw new ArgumentException( "Path mismatch.", nameof( subPath ) );
            }
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
        /// The short path to display, relative to a well known root.
        /// </summary>
        public NormalizedPath SubPath { get; }

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

        string IGitHeadInfo.GetSha( string path )
        {
            if( path == null ) return Git.Head.Tip.Sha;
            if( path.Length == 0 ) return Git.Head.Tip.Tree.Sha;
            var e = Git.Head.Tip.Tree[path];
            return e?.Target.Sha;
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
            if( Git.RetrieveStatus().IsDirty )
            {
                m.Error( $"Repository '{SubPath}' has uncommitted changes ({CurrentBranchName})." );
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gets the sha of the given branch tip or null if the branch doesn't exist.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The branch name. Must not be null or empty.</param>
        /// <returns>The Sha or null.</returns>
        public string GetBranchSha( IActivityMonitor m, string branchName )
        {
            if( String.IsNullOrWhiteSpace( branchName ) ) throw new ArgumentNullException( nameof( branchName ) );
            var b = GetBranch( m, branchName, false );
            return b?.Tip.Sha;
        }

        Branch GetBranch( IActivityMonitor m, string branchName, bool logErrorMissingLocalAndRemote )
        {
            return DoGetBranch( m, Git, branchName, logErrorMissingLocalAndRemote, SubPath );
        }

        static Branch DoGetBranch( IActivityMonitor m, Repository r, string branchName, bool logErrorMissingLocalAndRemote, string repoDisplayName )
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
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The branch name.</param>
        public void EnsureBranch( IActivityMonitor m, string branchName, bool noWarnOnCreate = false )
        {
            DoEnsureBranch( m, Git, branchName, noWarnOnCreate, SubPath );
        }

        /// <summary>
        /// Ensure that a branch exists. If the branch is created, it will point at the same commit as the current <see cref="Head"/>.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="r">The repository.</param>
        /// <param name="branchName">The name of the branch.</param>
        /// <param name="noWarnOnCreate">Log as warning if the branch is created.</param>
        /// <param name="repoDisplayName">Name of the repo displayed in the logs.</param>
        /// <returns>The Branch.</returns>
        static Branch DoEnsureBranch( IActivityMonitor m, Repository r, string branchName, bool noWarnOnCreate, string repoDisplayName )
        {
            if( String.IsNullOrWhiteSpace( branchName ) ) throw new ArgumentNullException( nameof( branchName ) );
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
            using( m.OpenInfo( $"Fetching {(originOnly ? "origin" : "all remotes")} in repository '{SubPath}'." ) )
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
        /// Pulls current branch by merging changes from remote 'orgin' branch into this repository.
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
            using( m.OpenInfo( $"Pulling branch '{CurrentBranchName}' in '{SubPath}'." ) )
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
            using( m.OpenInfo( $"Checking out branch '{branchName}' in '{SubPath}'." ) )
            {
                if( !skipFetchBranches && !FetchBranches( m ) ) return (false, false);
                try
                {
                    bool reloadNeeded = false;
                    Branch b = GetBranch( m, branchName, logErrorMissingLocalAndRemote: true );
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
            if( Git.Branches.Count() == 1 && Git.Branches.Single().TrackedBranch?.Tip == null )
            {
                //The remote repository is not initialized and have 0 commits.
                //We can't pull since there is only 1 branch, and this branch is local.
                Debug.Assert( !Git.Branches.Single().IsRemote );
                Debug.Assert( Git.Branches.Single().FriendlyName == "master" );
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
        public bool Commit( IActivityMonitor m, string commitMessage, CommitBehavior commitBehavior = CommitBehavior.CreateNewCommit )
        {
            if( commitBehavior != CommitBehavior.CreateNewCommit && CanAmendCommit )
            {
                Func<string, string> modified = null;
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
            using( m.OpenInfo( $"Committing changes in '{SubPath}' (branch '{CurrentBranchName}')." ) )
            {
                Commands.Stage( Git, "*" );
                var s = Git.RetrieveStatus();
                if( !s.IsDirty )
                {
                    m.CloseGroup( "Working folder is up-to-date." );
                    return true;
                }
                return DoCommit( m, commitMessage, DateTimeOffset.Now, false, true );
            }
        }

        /// <summary>
        /// Amends the current commit, optionaly changing its message and/or its date.
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
        public bool AmendCommit(
            IActivityMonitor m,
            Func<string, string> editMessage = null,
            Func<DateTimeOffset, DateTimeOffset?> editDate = null,
            bool skipIfNothingToCommit = true )
        {
            if( !CanAmendCommit ) throw new InvalidOperationException( nameof( CanAmendCommit ) );
            using( m.OpenInfo( $"Amending Commit in '{SubPath}' (branch '{CurrentBranchName}')." ) )
            {
                var message = Git.Head.Tip.Message;
                if( editMessage != null ) message = editMessage( message );
                if( String.IsNullOrWhiteSpace( message ) )
                {
                    m.CloseGroup( "Canceled by empty message." );
                    return false;
                }
                DateTimeOffset initialDate = Git.Head.Tip.Committer.When;
                DateTimeOffset? date = initialDate;
                if( editDate != null ) date = editDate( date.Value );
                if( date == null )
                {
                    m.CloseGroup( "Canceled by null date." );
                    return false;
                }
                Commands.Stage( Git, "*" );
                var s = Git.RetrieveStatus();
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
                        return true;
                    }
                }
                return DoCommit( m, message, date.Value, true, hasChange );
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="m"></param>
        /// <param name="commitMessage"></param>
        /// <param name="date"></param>
        /// <param name="amendPreviousCommit"></param>
        /// <param name="isDirty"></param>
        /// <returns>False on error. True otherwise.</returns>
        bool DoCommit( IActivityMonitor m, string commitMessage, DateTimeOffset date, bool amendPreviousCommit, bool isDirty )
        {
            try
            {
                if( isDirty ) m.Info( "Working Folder is dirty. Committing changes." );
                Signature author = amendPreviousCommit ? Git.Head.Tip.Author : Git.Config.BuildSignature( date );
                // Let AllowEmptyCommit even when amending: this avoids creating an empty commit.
                // If we are not amending, this is an error and we let the EmptyCommitException pops.
                var options = new CommitOptions { AmendPreviousCommit = amendPreviousCommit };
                var committer = new Signature( "CKli", "none", date );
                try
                {
                    Git.Commit( commitMessage, author ?? committer, committer, options );
                }
                catch( EmptyCommitException )
                {
                    if( !amendPreviousCommit ) throw;
                    Debug.Assert( Git.Head.Tip.Parents.Count() == 1, "This check on merge commit is already done by LibGit2Sharp." );
                    m.Trace( "No actual changes. Reseting branch to parent commit." );
                    Git.Reset( ResetMode.Hard, Git.Head.Tip.Parents.Single() );
                    Debug.Assert( options.AmendPreviousCommit = true );
                    Git.Commit( commitMessage, author, committer, options );
                    return true;
                }
                return true;
            }
            catch( Exception ex )
            {
                m.Error( ex );
                return false;
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
            if( branchName == null ) throw new ArgumentNullException( nameof( branchName ) );
            using( m.OpenInfo( $"Pushing '{SubPath}' (branch '{branchName}') to origin." ) )
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
        /// <param name="m">The monitor to use.</param>
        /// <param name="git">The Git key.</param>
        /// <param name="workingFolder">The local working folder.</param>
        /// <param name="ensureHooks">True to create the standard hooks.</param>
        /// <param name="branchName">
        /// The initial branch name if cloning is done.
        /// This branch is created if needed (just like <see cref="EnsureBranch"/> does).
        /// </param>
        /// <returns>The LibGit2Sharp repository object or null on error.</returns>
        public static Repository EnsureWorkingFolder(
            IActivityMonitor m,
            GitRepositoryKey git,
            NormalizedPath workingFolder,
            bool ensureHooks,
            string branchName = null )
        {
            using( m.OpenTrace( $"Ensuring working folder '{workingFolder}' on '{git.OriginUrl}'." ) )
            {
                try
                {
                    var gitFolderPath = Path.Combine( workingFolder, ".git" );
                    bool repoCreated = false;
                    using( m.OpenTrace( "Ensuring the git repository." ) )
                    {
                        if( !Directory.Exists( gitFolderPath ) )
                        {
                            m.Trace( $"The folder '{gitFolderPath}' does not exist." );
                            using( m.OpenInfo( $"Cloning '{workingFolder}' from '{git.OriginUrl}' on {branchName}." ) )
                            {
                                try
                                {
                                    Repository.Clone( git.OriginUrl.AbsoluteUri, workingFolder, new CloneOptions()
                                    {
                                        CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( m, git ),
                                        Checkout = true
                                    } );
                                    repoCreated = true;
                                }
                                catch( Exception ex )
                                {
                                    m.Error( "Git clone failed. Leaving existing directory as-is.", ex );
                                    return null;
                                }
                            }
                        }
                    }
                    Repository r;
                    using( m.OpenTrace( "Checking the validity of the git repository." ) )
                    {
                        if( !Repository.IsValid( gitFolderPath ) )
                        {
                            m.Fatal( $"Git folder '{gitFolderPath}' exists but is not a valid Repository." );
                            return null;
                        }
                        r = new Repository( workingFolder );
                        var remote = r.Network.Remotes.FirstOrDefault( rem => GitRepositoryKey.IsEquivalentRepositoryUri( new Uri( rem.Url, UriKind.Absolute ), git.OriginUrl ) );
                        if( remote == null || remote.Name != "origin" )
                        {

                            m.Fatal( $"Existing '{workingFolder}' must have its 'origin' remote set to '{git.OriginUrl}'. This must be fixed manually." );
                            r.Dispose();
                            return null;
                        }
                        if( !r.Commits.Any() )
                        {
                            m.Info( $"Unitialized repository: automatically creating an initial commit." );
                            var date = DateTimeOffset.Now;
                            Signature author = r.Config.BuildSignature( date );
                            var committer = new Signature( "CKli", "none", date );
                            r.Commit( "Initial commit automatically created.", author, committer, new CommitOptions { AllowEmptyCommit = true } );
                        }
                    }
                    if( r.Head?.FriendlyName != branchName && branchName != null )
                    {
                        Branch branch = DoEnsureBranch( m, r, branchName, false, workingFolder );
                        if( repoCreated ) Commands.Checkout( r, branch );
                    }
                    if( ensureHooks ) EnsureHooks( m, workingFolder );
                    m.CloseGroup( "Repository is checked out." );
                    return r;
                }
                catch( Exception ex )
                {
                    m.Fatal( $"Failed to ensure '{workingFolder}'.", ex );
                    return null;
                }
            }
        }

        /// <summary>
        /// Credentials is read from the <see cref="GitRepositoryKey.SecretKeyStore"/>.
        /// This can not be implemented by GitRepositoryKey since LibGit2Sharp is not
        /// a dependency of CK.Env.Sys. 
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="git">The repository key.</param>
        /// <returns>The Credentials object that is null or a <see cref="UsernamePasswordCredentials"/>.</returns>
        internal static Credentials? PATCredentialsHandler( IActivityMonitor m, GitRepositoryKey git )
        {
            string? pat = git.SecretKeyStore.GetSecretKey( m, git.ReadPATKeyName, !git.IsPublic );
            return pat != null
                    ? new UsernamePasswordCredentials() { Username = "CKli", Password = pat }
                    : null;
        }

        /// <summary>
        /// Binds this <see cref="RepositoryKey"/> to the static <see cref="PATCredentialsHandler(IActivityMonitor, GitRepositoryKey)"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The Credentials object that is null or a <see cref="UsernamePasswordCredentials"/>.</returns>
        protected Credentials PATCredentialsHandler( IActivityMonitor m ) => PATCredentialsHandler( m, RepositoryKey );


        #region GitHooks

        static void EnsureHooks( IActivityMonitor m, NormalizedPath root )
        {
            const string prepush = "pre-push";
            EnsureHookDispatcher( m, root, prepush );
            EnsureHookFile( m, root, prepush, "check_not_local_branch", _hook_check_not_local );
            EnsureHookFile( m, root, prepush, "check_no_commit_nopush", _hook_check_no_commit_nopush );
        }


        static SVersion CheckHookDispatcherVersion( string scriptString )
        {
            string[] lines = scriptString.Split( '\n' );
            if( lines.Length <= 2 ) return null;
            if( !lines[1].Contains( "script-dispatcher-" ) ) return null;
            if( !SVersion.TryParse(
                lines[1].Replace( "#script-dispatcher-", "" )
                .Trim(),
                out SVersion version ) )
            {
                throw new InvalidDataException( "Git hook script version badly forged" );
            }
            return version;
        }
        static NormalizedPath GetHooksDir( NormalizedPath root ) => root.AppendPart( ".git" ).AppendPart( "hooks" );

        static NormalizedPath GetHookPath( NormalizedPath root, string hookName ) => GetHooksDir( root ).AppendPart( hookName );

        static NormalizedPath GetMultiHooksDir( NormalizedPath root, string hookName ) => GetHooksDir( root ).AppendPart( hookName + "_scripts" );

        static void EnsureHookDispatcher( IActivityMonitor m, NormalizedPath root, string hookName )
        {
            NormalizedPath hookPath = GetHookPath( root, hookName );
            NormalizedPath multiHookDirectory = GetMultiHooksDir( root, hookName );
            bool hookPresent = File.Exists( hookPath );
            Directory.CreateDirectory( multiHookDirectory );
            if( hookPresent )
            {
                string installedScript = File.ReadAllText( hookPath );
                SVersion installedScriptVersion = CheckHookDispatcherVersion( installedScript );
                //replacing the hook dispatcher on a new version is not implemented yet, we just save the script to not destroy user hooks.
                if( installedScriptVersion == null )
                {
                    File.Move( hookPath, Path.Combine( multiHookDirectory, hookName ) );
                    m.Info( $"Git hook {hookName} was not our dispatcher. Moved user hook to {multiHookDirectory}." );
                }
                else
                {
                    SVersion currentScriptVersion = CheckHookDispatcherVersion( HookPrePushScript( hookName ) );
                    if( currentScriptVersion > installedScriptVersion )
                    {
                        m.Info( $"Git hook {hookName} was an old dispatcher. Removing it." );
                        File.Delete( hookPath );
                    }
                    else
                    {
                        m.Trace( $"The current {hookName} hook is our dispatcher." );
                    }
                }
            }
            // Now hook file may not exist event if it did.
            hookPresent = File.Exists( hookPath );
            if( !hookPresent )
            {
                File.WriteAllText( hookPath, HookPrePushScript( hookName ) );
                m.Info( $"Created dispatcher for {hookName} hooks." );
            }
        }

        static void EnsureHookFile( IActivityMonitor m, NormalizedPath root, string hookName, string scriptName, string script )
        {
            var hookPath = Path.Combine( GetMultiHooksDir( root, hookName ), scriptName );
            bool currentHookIsUpToDate = File.Exists( hookPath ) && File.ReadAllText( hookPath ) == script;
            if( !currentHookIsUpToDate )
            {
                m.Info( "git " + Path.GetFileName( hookPath ) + " hook not up to date. Updating!" );
                File.WriteAllText( hookPath, script );
            }
            else
            {
                m.Trace( $"The script {hookName}_scripts/{scriptName} is up to date." );
            }
        }

        const string _hook_check_not_local =
@"#!/bin/sh
# Abort push on local branches
while read local_ref local_sha remote_ref remote_sha 
do 
	regexp='(?i)(^|[^a-z0-9]+)local($|[^a-z0-9]+)'
	result=$( echo ""$local_ref"" | grep -P $regexp )
	if [[ ""$result"" != """" ]];
	then
		RED='\e[33;41m'
		NC='\e[0m' # No Color
		echo -e >&2 ""${RED}Pushing on a local branch, aborting!${NC}""
        exit 1
	fi
done
exit 0

";
        const string _hook_check_no_commit_nopush =
@"#!/bin/sh
# inspired from https://github.com/bobgilmore/githooks/blob/master/pre-push
# Hook stopping the push if we find a [NOPUSH] commit.
remote=""$1""
url=""$2""

z40=0000000000000000000000000000000000000000

echo ""Checking if a commit contain NOPUSH...""
while read local_ref local_sha remote_ref remote_sha
do
	if [ ""$local_sha"" = $z40 ]
	then
		echo ""Deleting files, OK.""
	else
		if [ ""$remote_sha"" = $z40 ]
		then
			# New branch, examine all commits
			range=""$local_sha""
		else
			# Update to existing branch, examine new commits
			range=""${remote_sha}..${local_sha}""
		fi

		# Check for foo commit
		commit=`git rev-list -n 1 --grep 'NOPUSH' ""$range""`
    echo $commit
		if [ -n ""$commit"" ]
		then
			echo >&2 ""ERROR: Found commit message containing 'NOPUSH' in $local_ref so you should not push this commit !!!""
      echo >&2 ""Commit containing the message: $commit""
			exit 1
		fi
	fi
done
echo ""No commit found with NOPUSH. Push can continue.""
exit 0
";
        static string HookPrePushScript( string hookName ) =>
$@"#!/bin/sh
#script-dispatcher-0.0.3
# Hook that execute all scripts in a directory
remote=""$1"";
url=""$2"";
hook_directory="".git/hooks""
search_dir=""pre-push_scripts""
search_path=""$hook_directory/$search_dir""
i=0
stdin=`cat`
for scriptFile in ""$search_path""/*; do
  i=$((i+=1))
  echo ""Running script $scriptFile"";
  echo ""$stdin"" | $scriptFile $@;  # execute successfully or break
    # Or more explicitly: if this execution fails, then stop the `for`:
   exitCode=$?
   if [ $exitCode -ne 0 ] ; then
   echo >&2 ""Script $scriptFile exit code is $exitCode. Aborting push."";
   exit $exitCode;
   fi
done
echo ""Executed successfully $i scripts.""
exit 0
";

        #endregion GitHooks
    }

}
