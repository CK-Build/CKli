using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CK.Env
{
    /// <summary>
    /// Abstract base that adds useful methods to LibGit2Sharp <see cref="Repository"/>.
    /// This is abstract since this base doesn't impose IDisposable support, it is up to
    /// specialized classes to handle disposing.
    /// </summary>
    public abstract class GitHelper : IGitHeadInfo
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
            Git = libRepository ?? throw new ArgumentNullException( nameof( libRepository ) ); ;
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
        /// Full physical path is the same as <see cref="RepositoryInformation.WorkingDirectory"/>.
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
                m.Error( $"Repository '{SubPath}' has uncommited changes ({CurrentBranchName})." );
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gets the sha of the given branch tip or null if the branch doesnt' exist.
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

        static Branch DoGetBranch( IActivityMonitor m, Repository r, string branchName, bool logErrorMissingLocalAndRemote, NormalizedPath subPath )
        {
            var b = r.Branches[branchName];
            if( b == null )
            {
                string remoteName = "origin/" + branchName;
                var remote = r.Branches[remoteName];
                if( remote == null )
                {
                    var msg = $"Repository '{subPath}': Both local '{branchName}' and remote '{remoteName}' not found.";
                    if( logErrorMissingLocalAndRemote ) m.Error( msg );
                    else m.Warn( msg );
                    return null;
                }
                m.Info( $"Creating local branch on remote '{remoteName}' in repository '{subPath}'." );
                b = r.Branches.Add( branchName, remote.Tip );
                b = r.Branches.Update( b, u => u.TrackedBranch = remote.CanonicalName );
            }
            return b;
        }

        /// <summary>
        /// Ensures that a local branch exists.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The branch name.</param>
        public void EnsureBranch( IActivityMonitor m, string branchName, bool noWarnOnCreate = false )
        {
            DoEnsureBranch( m, Git, branchName, noWarnOnCreate, SubPath );
        }

        static void DoEnsureBranch( IActivityMonitor m, Repository r, string branchName, bool noWarnOnCreate, NormalizedPath subPath )
        {
            if( String.IsNullOrWhiteSpace( branchName ) ) throw new ArgumentNullException( nameof( branchName ) );
            var b = DoGetBranch( m, r, branchName, logErrorMissingLocalAndRemote: false, subPath: subPath );
            if( b == null )
            {
                m.Log( noWarnOnCreate ? Core.LogLevel.Info : Core.LogLevel.Warn, $"Branch '{branchName}' does not exist. Creating local branch." ); ;
                b = r.CreateBranch( branchName );
            }
            Commands.Checkout( r, b );
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
                    m.Error( ex );
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
                    return DoPull( m, false, mergeFileFavor );
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
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        public (bool Success, bool ReloadNeeded) Checkout( IActivityMonitor m, string branchName )
        {
            using( m.OpenInfo( $"Checking out branch '{branchName}' in '{SubPath}'." ) )
            {
                if( !FetchBranches( m ) ) return (false, false);
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
                        m.Info( $"Checking out {branchName} (leaving {CurrentBranchName})." );
                        Commands.Checkout( Git, b );
                        OnNewCurrentBranch( m );
                        reloadNeeded = true;
                    }
                    return DoPull( m, reloadNeeded, MergeFileFavor.Theirs );
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return (false, true);
                }
            }
        }

        (bool Success, bool ReloadNeeded) DoPull( IActivityMonitor m, bool alreadyReloadNeeded, MergeFileFavor mergeFileFavor )
        {
            var merger = Git.Config.BuildSignature( DateTimeOffset.Now ) ?? new Signature( "CKli", "none", DateTimeOffset.Now );
            if( Git.Head.TrackedBranch.Tip == null )
            {
                m.Warn( "This branch has no tracking branch. Skipping pull." );
                return (false, false);
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
            return (true, alreadyReloadNeeded || result.Status != MergeStatus.UpToDate);
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
                        modified = p => $"{p} (...)\r\n{commitMessage}";
                        break;
                    case CommitBehavior.AmendIfPossibleAndPrependPreviousMessage:
                        if( string.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
                        modified = p => $"{commitMessage}(...)\r\n{p}";
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
        /// <returns>True on success, false on error.</returns>
        public bool AmendCommit( IActivityMonitor m, Func<string, string> editMessage = null, Func<DateTimeOffset, DateTimeOffset?> editDate = null )
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
                            m.Trace( "Adusted commit date to the next second." );
                            date = minDate;
                        }
                    }
                }
                else
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
                    else
                    {
                        m.CloseGroup( "Working folder is up-to-date." );
                        return true;
                    }
                }
                return DoCommit( m, message, date.Value, true, hasChange );
            }
        }

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
        public bool CanPush => RepositoryKey.SecretKeyStore.IsSecretKeyAvailable( RepositoryKey.WritePATKeyName ) == true
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
            string branchName = null)
        {
            using( m.OpenTrace( $"Ensuring working folder '{workingFolder}' on '{git.OriginUrl}'." ) )
            {
                try
                {
                    var gitFolderPath = Path.Combine( workingFolder, ".git" );
                    if( !Directory.Exists( gitFolderPath ) )
                    {
                        using( m.OpenInfo( $"Checking out '{workingFolder}' from '{git.OriginUrl}' on {branchName}." ) )
                        {
                            Repository.Clone( git.OriginUrl.ToString(), workingFolder, new CloneOptions()
                            {
                                CredentialsProvider = ( url, user, cred ) => PATCredentialsHandler( m, git ),
                                Checkout = true
                            } );
                        }
                    }
                    else if( !Repository.IsValid( gitFolderPath ) )
                    {
                        throw new InvalidOperationException( $"Git folder {gitFolderPath} exists but is not a valid Repository." );
                    }
                    Repository r = new Repository( workingFolder );
                    var remote = r.Network.Remotes.FirstOrDefault( rem => rem.Url.Equals( git.OriginUrl.ToString(), StringComparison.OrdinalIgnoreCase ) );
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
                    if( r.Head?.FriendlyName != branchName )
                    {
                        DoEnsureBranch( m, r, branchName, false, workingFolder );
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
        internal static Credentials PATCredentialsHandler( IActivityMonitor m, GitRepositoryKey git )
        {
            if( git.KnownGitProvider == KnownGitProvider.Unknown ) throw new InvalidOperationException( "Unknown Git provider." );
            string pat = git.SecretKeyStore.GetSecretKey( m, git.ReadPATKeyName, !git.IsPublic );
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
            const string prepush = "pre_push";
            EnsureHookDispatcher( m, root, prepush );
            EnsureHookFile( m, root, prepush, "check_not_local_branch", _hook_check_not_local );
            EnsureHookFile( m, root, prepush, "check_no_commit_nopush", _hook_check_no_commit_nopush );
        }


        static bool CheckHookDispatcherVersion( string scriptString )
        {
            string[] lines = scriptString.Split( '\n' );
            if( lines.Length <= 2 ) return false;
            if( lines[1].Contains( "#script-dispatcher-0.0.0" ) ) return true;
            if( lines[1].Contains( "script-dispatcher" ) )
            {
                throw new NotImplementedException( "Upgrade version of hook dispatcher is not implemented yet." );
            }
            return false;
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
                string currentScript = File.ReadAllText( hookPath );
                if( currentScript == _hook_check_not_local )
                {
                    m.Info( "Detected our old prepush script. It's not an userscript, we can remove it." );
                    File.Delete( hookPath );
                    hookPresent = false;
                }
                else
                {
                    //replacing the hook dispatcher on a new version is not implemented yet, we just save the script to not destroy user hooks.
                    if( !CheckHookDispatcherVersion( currentScript ) )
                    {
                        File.Move( hookPath, Path.Combine( multiHookDirectory, hookName ) );
                        m.Info( $"Git hook {hookName} was not the dispatcher. Moved user hook to {multiHookDirectory}." );
                        hookPresent = false;
                    }
                    else
                    {
                        m.Trace( $"The current {hookName} hook is our dispatcher." );
                    }
                }
            }
            // Now hook file may not exist event if it did.
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
# Abort push on -local branches

while read local_ref local_sha remote_ref remote_sha 
do 
	if [[ $local_ref == *-local ]]
	then
		echo >&2 ""Pushing on a -local branch, aborting!""
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
			range=""$remote_sha..$local_sha""
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
exit 1
";
        static string HookPrePushScript( string hookName ) =>
$@"#!/bin/sh
#script-dispatcher-0.0.0
# Hook that execute all scripts in a directory

remote=""$1"";
url=""$2"";
hook_directory="".git/hooks""
search_dir=""{hookName}_scripts""

search_path=""$hook_directory/$search_dir""
i=0
for scriptFile in ""$search_path""/*; do
  i=$((i+=1))
  echo ""Running script $scriptFile"";
  exitCode=exec ""$scriptFile"" ""$@"" || break;  # execute successfully or break
  # Or more explicitly: if this execution fails, then stop the `for`:
   if ! bash ""$scriptFile""; then
   >&2 echo ""Script $scriptFile failed. Aborting push."";
   exit $exitCode;
   break;
   fi
done
echo ""Executed successfully $i scripts.""
exit 1
";

        #endregion GitHooks
    }

}
