using CK.Core;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Primitives;
using LibGit2Sharp;
using System.Collections;
using System.Linq;
using CK.Text;
using LibGit2Sharp.Handlers;
using System.Xml.Linq;
using Microsoft.Alm.Authentication;
using SimpleGitVersion;
using CSemVer;

namespace CK.Env
{
    /// <summary>
    /// Implements Git repository mapping.
    /// GitFolder are internally created (and disposed) by <see cref="FileSystem"/>.
    /// </summary>
    public class GitFolder : IGitRepository, IGitHeadInfo, ICommandMethodsProvider
    {
        static internal readonly CredentialsHandler DefaultCredentialHandler;

        readonly Repository _git;
        readonly RootDir _thisDir;
        readonly HeadFolder _headFolder;
        readonly BranchesFolder _branchesFolder;
        readonly RemotesFolder _remoteBranchesFolder;
        readonly CommandRegister _commandRegister;
        bool _branchRefreshed;

        static GitFolder()
        {
            var secrets = new SecretStore( "git" );
            var auth = new BasicAuthentication( secrets );
            DefaultCredentialHandler = ( url, user, cred ) =>
            {
                var uri = new Uri( url ).GetLeftPart( UriPartial.Authority );
                var creds = auth.GetCredentials( new TargetUri( uri ) );
                return creds != null
                        ? new UsernamePasswordCredentials { Username = creds.Username, Password = creds.Password }
                        : null;
            };
        }

        internal GitFolder(
            FileSystem fs,
            string gitFolder,
            CommandRegister commandRegister,
            IWorldName world )
        {
            Debug.Assert( gitFolder.StartsWith( fs.Root.Path ) && gitFolder.EndsWith( ".git" ) );
            FullPhysicalPath = new NormalizedPath( gitFolder.Remove( gitFolder.Length - 4 ) );
            SubPath = FullPhysicalPath.RemovePrefix( fs.Root );
            if( SubPath.IsEmpty ) throw new InvalidOperationException( "Root path can not be a Git folder." );

            FileSystem = fs;
            World = world;
            _git = new Repository( gitFolder );
            _commandRegister = commandRegister;
            _headFolder = new HeadFolder( this );
            _branchesFolder = new BranchesFolder( this, "branches", isRemote: false );
            _remoteBranchesFolder = new RemotesFolder( this );
            _thisDir = new RootDir( this, SubPath.LastPart );
            ServiceContainer = new SimpleServiceContainer( FileSystem.ServiceContainer );
            ServiceContainer.Add( this );
            PluginManager = new GitPluginManager( ServiceContainer, commandRegister, world.DevelopBranchName, SubPath.AppendPart( "branches" ) );

            string origin = _git.Network.Remotes["origin"]?.Url;
            if( origin != null )
            {
                if( origin.IndexOf( "github.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitHub;
                else if( origin.IndexOf( "gitlab.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.GitLab;
                else if( origin.IndexOf( "dev.azure.com", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.Vsts;
                else if( origin.IndexOf( "bitbucket.org", StringComparison.OrdinalIgnoreCase ) >= 0 ) KnownGitProvider = KnownGitProvider.Bitbucket;
            }
            OriginUrl = origin;
            commandRegister.Register( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => SubPath;

        /// <summary>
        /// Gets the known Git provider.
        /// </summary>
        public KnownGitProvider KnownGitProvider { get; }

        /// <summary>
        /// Gets the service container for this GitFolder.
        /// </summary>
        public SimpleServiceContainer ServiceContainer { get; }

        /// <summary>
        /// Gets the plugin manager for this GitFolder and its branches.
        /// </summary>
        public GitPluginManager PluginManager { get; }

        /// <summary>
        /// Ensures that plugins are loaded for the <see cref="CurrentBranchName"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns></returns>
        public bool EnsureCurrentBranchPlugins( IActivityMonitor m )
        {
            if( CurrentBranchName != null )
            {
                return PluginManager.BranchPlugins.EnsurePlugins( m, CurrentBranchName, SubPath );
            }
            m.Error( $"No plugins since '{ToString()}' is not on a branch." );
            return false;
        }

        void CheckoutWithPlugins( IActivityMonitor m, Branch branch )
        {
            Commands.Checkout( _git, branch );
            EnsureCurrentBranchPlugins( m );
        }

        /// <summary>
        /// Gets the current remote origin url.
        /// </summary>
        public string OriginUrl { get; }

        /// <summary>
        /// Gets the current <see cref="IWorldName"/>.
        /// </summary>
        public IWorldName World { get; }

        /// <summary>
        /// Gets the full path (that starts with the <see cref="FileSystem"/>' root path) of the Git folder.
        /// </summary>
        public NormalizedPath FullPhysicalPath { get; }

        /// <summary>
        /// Get the path relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath SubPath { get; }

        /// <summary>
        /// Gets the name of the Git folder that is the name of the primary solution (by convention and by design).
        /// </summary>
        public string PrimarySolutionName => SubPath.LastPart;

        /// <summary>
        /// Gets the file system.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Fires whenever we switched to the local branch.
        /// </summary>
        public event EventHandler<EventMonitoredArgs> OnLocalBranchEntered;

        /// <summary>
        /// Fires whenever we are up to leave the local branch back to the develop one.
        /// </summary>
        public event EventHandler<EventMonitoredArgs> OnLocalBranchLeaving;

        /// <summary>
        /// Gets the current branch name (name of the repository's HEAD).
        /// </summary>
        public string CurrentBranchName => _git.Head.FriendlyName;

        /// <summary>
        /// Gets the standard git status, based on the <see cref="CurrentBranchName"/>.
        /// </summary>
        public StandardGitStatus StandardGitStatus => CurrentBranchName == World.LocalBranchName
                                                        ? StandardGitStatus.Local
                                                        : (CurrentBranchName == World.DevelopBranchName
                                                            ? StandardGitStatus.Develop
                                                            : StandardGitStatus.Unknwon);

        /// <summary>
        /// Gets the head information.
        /// </summary>
        public IGitHeadInfo Head => this;

        string IGitHeadInfo.CommitSha => _git.Head.Tip.Sha;

        string IGitHeadInfo.Message => _git.Head.Tip.Message;

        DateTimeOffset IGitHeadInfo.CommitDate => _git.Head.Tip.Committer.When;

        string IGitHeadInfo.GetSha( string path )
        {
            if( path == null ) return _git.Head.Tip.Sha;
            if( path.Length == 0 ) return _git.Head.Tip.Tree.Sha;
            var e = _git.Head.Tip.Tree[path];
            return e?.Target.Sha;
        }

        /// <summary>
        /// Gets the set of <see cref="PackageReleaseDiff"/> for packages from the current head.
        /// </summary>
        /// <param name="m">The minitor to use.</param>
        /// <param name="previousVersionCommitSha">Previous commit.</param>
        /// <param name="packages">Generated packages of a solution.</param>
        /// <returns>The set of diff or null on error.</returns>
        public IReadOnlyCollection<PackageReleaseDiff> GetReleaseDiff( IActivityMonitor m, string previousVersionCommitSha, IReadOnlyCollection<GeneratedPackage> packages )
        {
            if( packages.Count == 0 ) return Array.Empty<PackageReleaseDiff>();
            var r = new List<PackageReleaseDiff>();
            Commit commit = _git.Lookup<Commit>( previousVersionCommitSha );
            foreach( var p in packages )
            {
                var d = CreateDiff( m, commit, p );
                if( d == null ) return null;
                r.Add( d );
            }
            return r;
        }

        PackageReleaseDiff CreateDiff( IActivityMonitor m, Commit commit, GeneratedPackage p )
        {
            TreeEntry newTreeEntry = _git.Head.Tip.Tree[p.PrimarySolutionRelativeFolderPath];
            if( newTreeEntry == null || newTreeEntry.TargetType != TreeEntryTargetType.Tree )
            {
                m.Error( $"Unable to find {p.PrimarySolutionRelativeFolderPath}." );
                return null;
            }
            TreeEntry oldTreeEntry = commit.Tree[p.PrimarySolutionRelativeFolderPath];
            if( oldTreeEntry == null || oldTreeEntry.TargetType != TreeEntryTargetType.Tree )
            {
                m.Info( $"Previously {p.Name} did not exist." );
                return new PackageReleaseDiff( p, PackageReleaseDiffType.NewPackage );
            }
            Tree oldTree = (Tree)oldTreeEntry.Target;
            Tree newTree = (Tree)newTreeEntry.Target;
            if( oldTree.Sha == newTree.Sha )
            {
                return new PackageReleaseDiff( p, PackageReleaseDiffType.None );
            }
            using( TreeChanges changes = _git.Diff.Compare<TreeChanges>( oldTree, newTree ) )
            {
                var allChanges = changes.Select( gC => new FileReleaseDiff( gC.Path, (FileReleaseDiffType)gC.Status ) ).ToList();
                return new PackageReleaseDiff( p, allChanges );
            }
        }

        /// <summary>
        /// Checks that the current head is a clean commit (working directory is clean and no staging files exists).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True if the current head is clean, false otherwise.</returns>
        public bool CheckCleanCommit( IActivityMonitor m )
        {
            if( _git.RetrieveStatus().IsDirty )
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
            return b == null ? null : b.Tip.Sha;
        }

        Branch GetBranch( IActivityMonitor m, string branchName, bool logErrorMissingLocalAndRemote )
        {
            var b = _git.Branches[branchName];
            if( b == null )
            {
                string remoteName = "origin/" + branchName;
                var remote = _git.Branches[remoteName];
                if( remote == null )
                {
                    if( logErrorMissingLocalAndRemote ) m.Error( $"Repository '{SubPath}': Both local '{branchName}' and remote '{remoteName}' not found." );
                    return null;
                }
                m.Info( $"Creating local branch on remote '{remoteName}'." );
                b = _git.Branches.Add( branchName, remote.Tip );
                b = _git.Branches.Update( b, u => u.TrackedBranch = remote.CanonicalName );
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
        public bool FetchAll( IActivityMonitor m, bool originOnly = true )
        {
            using( m.OpenInfo( $"Fetching {(originOnly ? "origin" : "all remotes")} in repository '{SubPath}'." ) )
            {
                try
                {
                    foreach( Remote remote in _git.Network.Remotes.Where( r => !originOnly || r.Name == "origin" ) )
                    {
                        m.Info( $"Fetching remote '{remote.Name}'." );
                        IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select( x => x.Specification ).ToArray();
                        Commands.Fetch( _git, remote.Name, refSpecs, new FetchOptions()
                        {
                            CredentialsProvider = DefaultCredentialHandler,
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
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        public (bool Success, bool ReloadNeeded) Pull( IActivityMonitor m )
        {
            using( m.OpenInfo( $"Pulling branch '{CurrentBranchName}' in '{SubPath}'." ) )
            {
                if( !FetchAll( m )
                    || !CheckCleanCommit( m ) ) return (false, false);
                try
                {
                    return DoPull( m, false, FileSystem.ServerMode ? MergeFileFavor.Theirs : MergeFileFavor.Ours );
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
                if( !FetchAll( m ) ) return (false,false);
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
                        CheckoutWithPlugins( m, b );
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
            var merger = _git.Config.BuildSignature( DateTimeOffset.Now );
            var result = Commands.Pull( _git, merger, new PullOptions
            {
                FetchOptions = new FetchOptions
                {
                    TagFetchMode = TagFetchMode.All,
                    CredentialsProvider = DefaultCredentialHandler
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

        class RootTempBranch : IDisposable
        {
            readonly GitFolder _f;
            readonly RootTempBranch _parent;
            readonly Branch _temp;
            readonly Commit _commit;
            readonly IDisposableGroup _group;
            public readonly Branch OrginalHead;
            bool _closed;

            public RootTempBranch( GitFolder f, IDisposableGroup group )
            {
                _f = f;
                _group = group;
                var s = new Signature( "CKli", "none", DateTimeOffset.Now );
                if( f._tempBranch == null )
                {
                    OrginalHead = f._git.Head;
                    _temp = f._git.CreateBranch( $"TEMP_{Guid.NewGuid()}" );
                    Commands.Checkout( f._git, _temp );
                }
                else
                {
                    _parent = f._tempBranch;
                    OrginalHead = _parent.OrginalHead;
                    _temp = _parent._temp;
                }
                f._tempBranch = this;
                // IncludeIgnored = true is a very heavy operation...
                Commands.Stage( f._git, "*", new StageOptions() { IncludeIgnored = false } );
                _commit = f._git.Commit( "Temp", s, s, new CommitOptions() { AllowEmptyCommit = true, PrettifyMessage = false } );
            }

            void Close()
            {
                Debug.Assert( !_closed && _f._tempBranch == this );
                _f._git.Reset( ResetMode.Hard, _commit, new CheckoutOptions() { CheckoutModifiers = CheckoutModifiers.Force } );
                // Since we don't IncludeIgnored, we must not clear the untracked files.
                // _f._git.RemoveUntrackedFiles();

                if( _parent == null )
                {
                    _f._git.Reset( ResetMode.Mixed, OrginalHead.Tip );
                    Commands.Checkout( _f._git, OrginalHead );
                    _f._git.Branches.Remove( _temp );
                }
                else
                {
                    _f._git.Reset( ResetMode.Mixed, _parent._commit );
                }
                _f._tempBranch = _parent;
                _closed = true;
                _group?.Dispose();
            }

            public void Dispose()
            {
                if( !_closed )
                {
                    while( _f._tempBranch != this && _f._tempBranch != null ) _f._tempBranch.Dispose();
                    if( _f._tempBranch != null ) Close();
                }
            }
        }

        RootTempBranch _tempBranch;

        /// <summary>
        /// Opens a temporary branch that protects all changes.
        /// This can be called recursively.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="message">Message of the group information. Null to not open a group.</param>
        /// <returns>A disposable that must be disposed to restore the state of the working directory.</returns>
        public IDisposable OpenProtectedScope( IActivityMonitor m, string message = "Opening protected scope: any file modification will be reverted." )
        {
            return new RootTempBranch( this, message != null ? m.OpenInfo( message ) : null );
        }

        /// <summary>
        /// Gets whether at least one scope is currently opened (see <see cref="OpenProtectedScope"/>).
        /// </summary>
        public bool IsProtectedScopeOpened => _tempBranch != null;

        /// <summary>
        /// Gets the valid version information from a branch or null.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Defaults to <see cref="CurrentBranchName"/>.</param>
        /// <returns>The commit version info or null if it it cannot be obtained.</returns>
        public CommitVersionInfo GetCommitVersionInfo( IActivityMonitor m, string branchName = null )
        {
            var info = ReadRepositoryVersionInfo( m, branchName );
            return info != null
                    ? new CommitVersionInfo(
                            info.CommitSha,
                            info.ValidReleaseTag,
                            info.BetterExistingVersion?.ThisTag,
                            info.CommitInfo.BasicInfo?.BestCommitBelow?.ThisTag,
                            info.CommitInfo.BasicInfo?.BestCommitBelow?.CommitSha,
                            info.NextPossibleVersions,
                            info.PossibleVersions,
                            new CommitAssemblyBuildInfoFromRepo( info ) )
                    : null;
        }

        /// <summary>
        /// Gets the simple git version <see cref="RepositoryInfo"/> from a branch.
        /// Returns null if an error occurred or if RepositoryInfo.xml has not been successfully read.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Defaults to <see cref="CurrentBranchName"/>.</param>
        /// <returns>The RepositoryInfo or null if it it cannot be obtained.</returns>
        public RepositoryInfo ReadRepositoryVersionInfo( IActivityMonitor m, string branchName = null )
        {
            if( branchName == null ) branchName = CurrentBranchName;
            try
            {
                Branch b = _git.Branches[branchName];
                if( b == null )
                {
                    m.Error( $"Unknown branch {branchName}." );
                    return null;
                }
                var pathOpt = b.IsRemote
                                ? SubPath.AppendPart( "remotes" ).Combine( b.FriendlyName )
                                : SubPath.AppendPart( "branches" ).AppendPart( branchName );

                pathOpt = pathOpt.AppendPart( "RepositoryInfo.xml" );
                var fOpt = FileSystem.GetFileInfo( pathOpt );
                if( !fOpt.Exists )
                {
                    m.Error( $"Missing required {pathOpt} file." );
                    return null;
                }
                var opt = RepositoryInfoOptions.Read( fOpt.ReadAsXDocument().Root );
                opt.StartingBranchName = branchName;
                var result = new RepositoryInfo( _git, opt );
                if( result.RepositoryError != null )
                {
                    m.Error( $"Unable to read RepositoryInfo. RepositoryError: {result.RepositoryError}." );
                    return null;
                }
                if( result.Error != null )
                {
                    m.Error( result.ReleaseTagError );
                    return null;
                }
                return result;
            }
            catch( Exception ex )
            {
                m.Fatal( $"While reading version info for branch '{branchName}'.", ex );
                return null;
            }
        }

        /// <summary>
        /// Sets a version lightweight tag on the current head.
        /// An error is logged if the version tag already exists on another commit that the head.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="v">The version to set.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SetVersionTag( IActivityMonitor m, SVersion v )
        {
            var sv = 'v' + v.ToString();
            try
            {
                var exists = _git.Tags[sv];
                if( exists != null && exists.PeeledTarget == _git.Head.Tip )
                {
                    m.Info( $"Version Tag {sv} is already set." );
                    return true;
                }
                _git.ApplyTag( sv );
                m.Info( $"Set Version tag {sv} on {CurrentBranchName}." );
                return true;
            }
            catch( Exception ex )
            {
                m.Error( $"SetVersionTag {sv} on {CurrentBranchName} failed.", ex );
                return false;
            }
        }

        /// <summary>
        /// Pushes a version lightweight tag to the 'origin' remote.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="v">The version to push.</param>
        /// <returns>True on success, false on error.</returns>
        public bool PushVersionTag( IActivityMonitor m, SVersion v )
        {
            var sv = 'v' + v.ToString();
            using( m.OpenInfo( $"Pushing tag {sv} to remote for {SubPath}." ) )
            {
                try
                {
                    Remote remote = _git.Network.Remotes["origin"];
                    var options = new PushOptions() { CredentialsProvider = DefaultCredentialHandler };

                    var exists = _git.Tags[sv];
                    if( exists == null )
                    {
                        m.Error( $"Version Tag {sv} does not exist in {SubPath}." );
                        return false;
                    }
                    _git.Network.Push( remote, exists.CanonicalName, options );
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( $"PushVersionTags failed ({sv} on {SubPath}).", ex );
                    return false;
                }
            }
        }

        /// <summary>
        /// Removes a version lightweight tag from the repository.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="v">The version to remove.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ClearVersionTag( IActivityMonitor m, SVersion v )
        {
            var sv = 'v' + v.ToString();
            try
            {
                _git.Tags.Remove( sv );
                m.Info( $"Removing Version tag '{sv}' from {SubPath}." );
                return true;
            }
            catch( Exception ex )
            {
                m.Error( $"ClearVersionTag {sv} on {SubPath} failed.", ex );
                return false;
            }
        }

        /// <summary>
        /// Gets whether the head can be amended: the current branch
        /// is not tracked or the current commit is ahead of the remote branch.
        /// </summary>
        public bool CanAmendCommit => (_git.Head.TrackingDetails.AheadBy ?? 1) > 0;

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
        public bool Commit( IActivityMonitor m, string commitMessage, bool amendIfPossible = false )
        {
            if( String.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
            if( amendIfPossible && CanAmendCommit )
            {
                return AmendCommit( m );
            }
            using( m.OpenInfo( $"Committing changes in '{SubPath}' (branch '{CurrentBranchName}')." ) )
            {
                Commands.Stage( _git, "*" );
                var s = _git.RetrieveStatus();
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
        public bool AmendCommit( IActivityMonitor m, Func<string, string> editMessage = null, Func<DateTimeOffset,DateTimeOffset?> editDate = null )
        {
            if( !CanAmendCommit ) throw new InvalidOperationException( nameof( CanAmendCommit ) );
            using( m.OpenInfo( $"Amending Commit in '{SubPath}' (branch '{CurrentBranchName}')." ) )
            {
                var message = _git.Head.Tip.Message;
                if( editMessage != null ) message = editMessage( message );
                if( String.IsNullOrWhiteSpace( message ) )
                {
                    m.CloseGroup( "Canceled by empty message." );
                    return false;
                }
                DateTimeOffset? date = _git.Head.Tip.Committer.When;
                if( editDate != null ) date = editDate( date.Value );
                if( date == null )
                {
                    m.CloseGroup( "Canceled by null date." );
                    return false;
                }
                Commands.Stage( _git, "*" );
                var s = _git.RetrieveStatus();
                bool hasChange = s.IsDirty;
                if( hasChange )
                {
                    if( editDate == null ) date = DateTimeOffset.Now;
                }
                else
                {
                    bool messageUpdate = message != _git.Head.Tip.Message;
                    bool dateUpdate = date.Value != _git.Head.Tip.Committer.When;
                    if( messageUpdate && dateUpdate ) m.Info( "Updating message and date." );
                    else if( dateUpdate ) m.Info( "Updating commit date." );
                    else if( messageUpdate ) m.Info( "Only updating message." );
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
                var author = amendPreviousCommit ? _git.Head.Tip.Author : _git.Config.BuildSignature( date );
                // Let AllowEmptyCommit even when amending: this avoids creating an empty commit.
                // If we are not amending, this is an error and we let the EmptyCommitException pops.
                var options = new CommitOptions { AmendPreviousCommit = amendPreviousCommit };
                var committer = new Signature( "CKli", "none", date );
                try
                {
                    _git.Commit( commitMessage, author, committer, options );
                }
                catch( EmptyCommitException ) 
                {
                    if( !amendPreviousCommit ) throw;
                    Debug.Assert( _git.Head.Tip.Parents.Count() == 1, "This check on merge commit is already done by LibGit2Sharp." );
                    m.Trace( "No actual changes. Reseting branch to parent commit." );
                    _git.Reset( ResetMode.Hard, _git.Head.Tip.Parents.Single() );
                    Debug.Assert( options.AmendPreviousCommit = true );
                    _git.Commit( commitMessage, author, committer, options );
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
        /// Pushes changes from a branch to the origin.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Local branch name. When null, the <see cref="CurrentBranchName"/> is used.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Push( IActivityMonitor m, string branchName = null )
        {
            if( branchName == null ) branchName = CurrentBranchName;
            using( m.OpenInfo( $"Pushing '{SubPath}' (branch '{branchName}') to origin." ) )
            {
                try
                {
                    var b = _git.Branches[branchName];
                    if( b == null )
                    {
                        m.Error( $"Unable to find branch '{branchName}'." );
                        return false;
                    }
                    if( !b.IsTracking )
                    {
                        m.Warn( $"Branch '{branchName}' does not exist on the remote. Creating the remote branch on 'origin'." );
                        _git.Branches.Update( b, u => { u.Remote = "origin"; u.UpstreamBranch = b.CanonicalName; } );
                    }
                    var options = new PushOptions()
                    {
                        CredentialsProvider = DefaultCredentialHandler
                    };
                    _git.Network.Push( b, options );
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
        /// Resets the index to the tree recorded by the commit and updates the working directory to
        /// match the content of the index.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ResetHard( IActivityMonitor m )
        {
            using( m.OpenInfo( $"Reset --hard changes in '{SubPath}' (branch '{CurrentBranchName}')." ) )
            {
                try
                {
                    _git.Reset( ResetMode.Hard );
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
        /// Resets a branch to a previous commit or deletes the branch when <paramref name="commitSha"/> is null or empty.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">The branch name.</param>
        /// <param name="commitSha">The commit sha to restore.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ResetBranchState( IActivityMonitor m, string branchName, string commitSha )
        {
            if( String.IsNullOrWhiteSpace( branchName ) ) throw new ArgumentNullException( nameof( branchName ) );
            bool delete = String.IsNullOrWhiteSpace( commitSha );
            using( m.OpenInfo( delete ? $"Restoring {SubPath} '{branchName}' state." : $"Restoring {SubPath} '{branchName}' state (removing it)." ) )
            {
                try
                {
                    if( delete )
                    {
                        if( branchName == CurrentBranchName )
                        {
                            m.Error( $"Cannot delete the branch {branchName} since it is the current one." );
                            return false;
                        }
                        var toDelete = _git.Branches[branchName];
                        if( toDelete == null )
                        {
                            m.Info( $"Branch '{branchName}' does not exist." );
                            return true;
                        }
                        _git.Branches.Remove( toDelete );
                        m.Info( $"Branch '{branchName}' has been removed." );
                        return true;
                    }
                    var current = _git.Head;
                    if( branchName == current.FriendlyName )
                    {
                        if( commitSha == current.Tip.Sha )
                        {
                            m.Info( $"Current branch '{current}' is alredy on restored state." );
                            return true;
                        }
                        _git.Reset( ResetMode.Hard, commitSha );
                        m.Info( $"Current branch '{current}' has been restored to {commitSha}." );
                        return true;
                    }
                    var b = _git.Branches[branchName];
                    if( b == null )
                    {
                        m.Warn( $"Branch '{branchName}' not found." );
                        return true;
                    }
                    if( commitSha == b.Tip.Sha )
                    {
                        m.Info( $"Current branch '{branchName}' is alredy on restored state." );
                        return true;
                    }
                    Commands.Checkout( _git, b );
                    _git.Reset( ResetMode.Hard, commitSha );
                    m.Info( $"Branch '{branchName}' has been restored to {commitSha}." );
                    Commands.Checkout( _git, current );
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
        /// Checkouts the <see cref="IWorldName.LocalBranchName"/>, always merging <see cref="IWorldName.DevelopBranchName"/> into it.
        /// If the repository is not on the 'local' branch, it must be on 'develop' (a <see cref="Commit"/> is done to save any
        /// current work if <paramref name="autoCommit"/> is true), the 'local' branch is created if needed and checked out.
        /// 'develop' branch is always merged into it, privilegiating file modifications from the 'develop' branch.
        /// If the the merge fails, a manual operation is required.
        /// On success, the solution inside should be purely local: there should not be any possible remote interactions (except
        /// possibly importing fully external packages).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="autoCommit">False to require the working folder to be clean and not automatically creating a commit.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchDevelopToLocal( IActivityMonitor m, bool autoCommit = true )
        {
            using( m.OpenInfo( $"Switching '{SubPath}' to branch '{World.LocalBranchName}')." ) )
            {
                try
                {
                    Branch develop = _git.Branches[World.DevelopBranchName];
                    if( develop == null )
                    {
                        m.Error( $"Unable to find branch '{World.DevelopBranchName}'." );
                        return false;
                    }
                    // Auto merge from Ours or Theirs: we privilegiate the current branch.
                    MergeFileFavor mergeFileFavor = MergeFileFavor.Normal;
                    Branch local = _git.Branches[World.LocalBranchName];
                    if( local == null || !local.IsCurrentRepositoryHead )
                    {
                        if( !develop.IsCurrentRepositoryHead )
                        {
                            m.Error( $"Expected current branch to be '{World.DevelopBranchName}' or '{World.LocalBranchName}'." );
                            return false;
                        }
                        if( autoCommit )
                        {
                            if( !Commit( m, $"Switching to {World.LocalBranchName} branch." ) ) return false;
                        }
                        else
                        {
                            if( !CheckCleanCommit( m ) ) return false;
                        }
                        if( local == null )
                        {
                            m.Info( $"Creating the {World.LocalBranchName}." );
                            local = _git.CreateBranch( World.LocalBranchName );
                        }
                        else
                        {
                            m.Info( "Coming from develop: privilegiates 'develop' file changes during merge." );
                            mergeFileFavor = MergeFileFavor.Theirs;
                        }
                        CheckoutWithPlugins( m, local );
                    }
                    else
                    {
                        m.Info( $"Already on {World.LocalBranchName}: privilegiates 'local' file changes during merge." );
                        mergeFileFavor = MergeFileFavor.Ours;
                        EnsureCurrentBranchPlugins( m );
                    }
                    var merger = _git.Config.BuildSignature( DateTimeOffset.Now );
                    var r = _git.Merge( develop, merger, new MergeOptions
                    {
                        MergeFileFavor = mergeFileFavor,
                        FastForwardStrategy = FastForwardStrategy.NoFastForward,
                        CommitOnSuccess = true,
                        FailOnConflict = true
                    } );
                    if( r.Status == MergeStatus.Conflicts )
                    {
                        m.Error( $"Merge failed from '{World.DevelopBranchName}' to '{World.LocalBranchName}': conflicts must be manually resolved." );
                        return false;
                    }

                    if( !RaiseEnteredLocalBranch( m, true ) ) return false;

                    if( !AmendCommit( m ) ) return false;
                    if( r.Status != MergeStatus.UpToDate )
                    {
                        m.CloseGroup( $"Success (with merge from '{World.DevelopBranchName}')." );
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

        bool RaiseEnteredLocalBranch( IActivityMonitor m, bool enter )
        {
            PluginManager.BranchPlugins.EnsurePlugins( m, World.LocalBranchName, SubPath );
            using( m.OpenTrace( $"{ToString()}: Raising {(enter ? "OnLocalBranchEntered" : "OnLocalBranchLeaving")} event." ) )
            {
                try
                {
                    bool hasError = false;
                    using( m.OnError( () => hasError = true ) )
                    {
                        if( enter )
                        {
                            OnLocalBranchEntered?.Invoke( this, new EventMonitoredArgs( m ) );
                        }
                        else
                        {
                            OnLocalBranchLeaving?.Invoke( this, new EventMonitoredArgs( m ) );
                        }
                    }
                    return !hasError;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
            }
        }

        /// <summary>
        /// Checkouts the <see cref="IWorldName.MasterBranchName"/>, always merging <see cref="IWorldName.DevelopBranchName"/> into it.
        /// If the repository is not already on the 'master' branch, it must be on 'develop' and on a clean commit.
        /// The 'master' branch is created if needed and checked out.
        /// 'develop' branch is always merged into it.
        /// If the the merge fails, a manual operation is required.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchDevelopToMaster( IActivityMonitor m )
        {
            using( m.OpenInfo( $"Switching '{SubPath}' to branch '{World.MasterBranchName}')." ) )
            {
                try
                {
                    Branch bDevelop = _git.Branches[World.DevelopBranchName];
                    if( bDevelop == null )
                    {
                        m.Error( $"Unable to find branch '{World.DevelopBranchName}'." );
                        return false;
                    }
                    Branch master = _git.Branches[World.MasterBranchName];
                    if( master == null || !master.IsCurrentRepositoryHead )
                    {
                        if( !bDevelop.IsCurrentRepositoryHead )
                        {
                            m.Error( $"Expected current branch to be '{World.DevelopBranchName}' or '{World.MasterBranchName}'." );
                            return false;
                        }
                        if( !CheckCleanCommit( m ) ) return false;
                        if( master == null )
                        {
                            m.Info( $"Creating the {World.MasterBranchName }." );
                            master = _git.CreateBranch( World.MasterBranchName );
                        }
                        CheckoutWithPlugins( m, master );
                    }
                    else m.Trace( $"Already on {World.MasterBranchName}." );

                    var merger = _git.Config.BuildSignature( DateTimeOffset.Now );
                    var r = _git.Merge( bDevelop, merger, new MergeOptions
                    {
                        FastForwardStrategy = FastForwardStrategy.NoFastForward,
                        CommitOnSuccess = true,
                        FailOnConflict = true
                    } );
                    if( r.Status == MergeStatus.Conflicts )
                    {
                        m.Error( $"Merge failed from '{World.DevelopBranchName}' to '{World.MasterBranchName}': conflicts must be manually resolved." );
                        return false;
                    }
                    if( r.Status != MergeStatus.UpToDate )
                    {
                        m.CloseGroup( $"Success (with merge from '{World.DevelopBranchName}')." );
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
        /// Checkouts '<see cref="IWorldName.DevelopBranchName"/>' branch (that must be clean)
        /// and merges current 'local' in it.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchLocalToDevelop( IActivityMonitor m )
        {
            using( m.OpenInfo( $"Switching '{SubPath}' to branch '{World.DevelopBranchName}'." ) )
            {
                try
                {
                    Branch bDevelop = _git.Branches[World.DevelopBranchName];
                    if( bDevelop == null )
                    {
                        m.Error( $"Unable to find '{World.DevelopBranchName}' branch." );
                        return false;
                    }
                    Branch bLocal = _git.Branches[World.LocalBranchName];
                    if( bLocal == null )
                    {
                        m.Error( $"Unable to find '{World.LocalBranchName}' branch." );
                        return false;
                    }
                    if( !bDevelop.IsCurrentRepositoryHead && !bLocal.IsCurrentRepositoryHead )
                    {
                        m.Error( $"Must be on '{World.LocalBranchName}' or '{World.DevelopBranchName}' branch." );
                        return false;
                    }

                    // Auto merge from Ours or Theirs: we privilegiate the current branch.
                    MergeFileFavor mergeFileFavor = MergeFileFavor.Normal;
                    if( bLocal.IsCurrentRepositoryHead )
                    {
                        if( !RaiseEnteredLocalBranch( m, false ) ) return false;
                        if( !AmendCommit( m ) ) return false;
                        CheckoutWithPlugins( m, bDevelop );
                        m.Info( $"Coming from {World.LocalBranchName}: privilegiates 'local' file changes during merge." );
                        mergeFileFavor = MergeFileFavor.Theirs;
                    }
                    else
                    {
                        if( !CheckCleanCommit( m ) ) return false;
                        EnsureCurrentBranchPlugins( m );
                        m.Info( $"Already on {World.DevelopBranchName}: privilegiates 'develop' file changes during merge." );
                        mergeFileFavor = MergeFileFavor.Ours;
                    }

                    var merger = _git.Config.BuildSignature( DateTimeOffset.Now );
                    var r = _git.Merge( bLocal, merger, new MergeOptions
                    {
                        MergeFileFavor = mergeFileFavor,
                        FastForwardStrategy = FastForwardStrategy.NoFastForward,
                        FailOnConflict = true,
                        CommitOnSuccess = true
                    } );
                    if( r.Status == MergeStatus.Conflicts )
                    {
                        m.Error( $"Merge failed from '{World.LocalBranchName}' into '{World.DevelopBranchName}': conflicts must be manually resolved." );
                        return false;
                    }
                    if( r.Status != MergeStatus.UpToDate )
                    {
                        m.CloseGroup( $"Success (with merge from '{World.LocalBranchName}')." );
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
        /// Simple safe check out of the <see cref="IWorldName.DevelopBranchName"/> (that must exist) from
        /// the <see cref="IWorldName.MasterBranchName"/> (that may not exist).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchMasterToDevelop( IActivityMonitor m )
        {
            using( m.OpenInfo( $"Switching '{SubPath}' to branch '{World.DevelopBranchName}'." ) )
            {
                try
                {
                    Branch develop = _git.Branches[World.DevelopBranchName];
                    if( develop == null )
                    {
                        m.Error( $"Unable to find '{World.DevelopBranchName}' branch." );
                        return false;
                    }
                    Branch master = _git.Branches[World.MasterBranchName];
                    if( !develop.IsCurrentRepositoryHead && (master == null || !master.IsCurrentRepositoryHead) )
                    {
                        m.Error( $"Must be on '{World.MasterBranchName}' or '{World.DevelopBranchName}' branch." );
                        return false;
                    }
                    if( master != null && master.IsCurrentRepositoryHead )
                    {
                        if( !CheckCleanCommit( m ) ) return false;
                        CheckoutWithPlugins( m, develop );
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

        internal void Dispose()
        {
            _commandRegister.Unregister( this );
            ((IDisposable)PluginManager).Dispose();
            _git.Dispose();
        }

        public override string ToString() => $"{FullPhysicalPath} ({CurrentBranchName ?? "<no branch>" }).";

        abstract class BaseDirFileInfo : IFileInfo
        {
            public BaseDirFileInfo( string name )
            {
                Name = name;
            }

            public bool Exists => true;

            public string Name { get; }

            bool IFileInfo.IsDirectory => true;

            long IFileInfo.Length => -1;

            public virtual string PhysicalPath => null;

            Stream IFileInfo.CreateReadStream()
            {
                throw new InvalidOperationException( "Cannot create a stream for a directory." );
            }

            public abstract DateTimeOffset LastModified { get; }

        }

        class RootDir : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitFolder _f;
            readonly IReadOnlyList<IFileInfo> _content;

            internal RootDir( GitFolder f, string name )
                : base( name )
            {
                _f = f;
                _content = new IFileInfo[] { f._headFolder, f._branchesFolder, f._remoteBranchesFolder };
            }

            public override DateTimeOffset LastModified => Directory.GetLastWriteTimeUtc( _f.FullPhysicalPath.Path );

            public IEnumerator<IFileInfo> GetEnumerator() => _content.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        }

        class HeadFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitFolder _f;
            IDirectoryContents _physical;

            public HeadFolder( GitFolder f )
                : base( "head" )
            {
                _f = f;
            }

            public override DateTimeOffset LastModified => _f._git.Head.Tip.Committer.When;

            public override string PhysicalPath => _f.FullPhysicalPath.Path;

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                if( _physical == null ) _physical = _f.FileSystem.PhysicalGetDirectoryContents( _f.SubPath );
                return _physical.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        }

        class CommitFolder : BaseDirFileInfo, IDirectoryContents
        {
            public readonly Commit Commit;

            public CommitFolder( string name, Commit c )
                : base( name )
            {
                Commit = c;
            }

            public override DateTimeOffset LastModified => Commit.Committer.When;

            public IFileInfo GetFileInfo( NormalizedPath sub )
            {
                var e = Commit.Tree[sub.ToString( '/' )];
                if( e != null && e.TargetType != TreeEntryTargetType.GitLink )
                {
                    return new TreeEntryWrapper( e, this );
                }
                return null;
            }

            public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
            {
                if( sub.IsEmpty ) return this;
                TreeEntry e = Commit.Tree[sub.ToString( '/' )];
                if( e != null && e.TargetType != TreeEntryTargetType.GitLink )
                {
                    return new TreeEntryWrapper( e, this );
                }
                return NotFoundDirectoryContents.Singleton;
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                return Commit.Tree.Select( t => new TreeEntryWrapper( t, this ) ).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        class RemotesFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitFolder _f;
            readonly SortedDictionary<string, BranchesFolder> _origins;

            public RemotesFolder( GitFolder f )
                : base( "remotes" )
            {
                _f = f;
                _origins = new SortedDictionary<string, BranchesFolder>();
            }

            internal void Add( Branch b )
            {
                string name = b.RemoteName;
                if( !_origins.TryGetValue( name, out var exists ) )
                {
                    _origins.Add( name, exists = new BranchesFolder( _f, name, true ) );
                }
                exists.Add( b );
            }

            public IFileInfo GetFileInfo( NormalizedPath sub )
            {
                if( sub.Parts.Count == 1 ) return this;
                if( _origins.TryGetValue( sub.Parts[1], out var origin ) )
                {
                    return origin.GetFileInfo( sub.RemoveFirstPart() );
                }
                return null;
            }

            public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
            {
                if( sub.Parts.Count == 1 ) return this;
                if( _origins.TryGetValue( sub.Parts[1], out var origin ) )
                {
                    return origin.GetDirectoryContents( sub.RemoveFirstPart() );
                }
                return NotFoundDirectoryContents.Singleton;
            }

            public override DateTimeOffset LastModified => FileUtil.MissingFileLastWriteTimeUtc;

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                _f.RefreshBranches();
                return _origins.Values.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        class BranchesFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitFolder _f;
            readonly SortedDictionary<string, CommitFolder> _branches;
            readonly bool _isRemote;

            public BranchesFolder( GitFolder f, string name, bool isRemote )
                : base( name )
            {
                _f = f;
                _branches = new SortedDictionary<string, CommitFolder>();
                _isRemote = isRemote;
            }

            public override DateTimeOffset LastModified => _f._git.Head.Tip.Committer.When;

            internal bool Add( Branch b )
            {
                Debug.Assert( b.IsRemote == _isRemote && (!_isRemote || b.RemoteName == Name) );
                string name = _isRemote ? b.FriendlyName.Remove( 0, Name.Length + 1 ) : b.FriendlyName;
                if( _branches.TryGetValue( name, out var exists ) )
                {
                    if( exists.Commit != b.Tip )
                    {
                        _branches[name] = new CommitFolder( name, b.Tip );
                    }
                }
                else _branches.Add( name, new CommitFolder( name, b.Tip ) );
                return true;
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                _f.RefreshBranches();
                return _branches.Values.GetEnumerator();
            }

            public IFileInfo GetFileInfo( NormalizedPath sub )
            {
                if( sub.Parts.Count == 1 ) return this;
                if( _branches.TryGetValue( sub.Parts[1], out var b ) )
                {
                    if( sub.Parts.Count == 2 ) return b;
                    return b.GetFileInfo( sub.RemoveParts( 0, 2 ) );
                }
                return null;
            }

            public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
            {
                if( sub.Parts.Count == 1 ) return this;
                if( _branches.TryGetValue( sub.Parts[1], out var b ) )
                {
                    return b.GetDirectoryContents( sub.RemoveParts( 0, 2 ) );
                }
                return NotFoundDirectoryContents.Singleton;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        class TreeEntryWrapper : IFileInfo, IDirectoryContents
        {
            readonly TreeEntry _e;
            readonly CommitFolder _c;

            public TreeEntryWrapper( TreeEntry e, CommitFolder c )
            {
                Debug.Assert( e.TargetType == TreeEntryTargetType.Blob || e.TargetType == TreeEntryTargetType.Tree );
                _e = e;
                _c = c;
            }

            Blob Blob => _e.Target as Blob;

            public bool Exists => true;

            public long Length => Blob?.Size ?? -1;

            public string PhysicalPath => null;

            public string Name => _e.Name;

            public DateTimeOffset LastModified => _c.LastModified;

            public bool IsDirectory => _e.TargetType == TreeEntryTargetType.Tree;

            public Stream CreateReadStream()
            {
                if( IsDirectory ) throw new InvalidOperationException();
                return Blob.GetContentStream();
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                if( !IsDirectory ) throw new InvalidOperationException();
                return ((Tree)_e.Target).Select( t => new TreeEntryWrapper( t, _c ) ).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        void RefreshBranches()
        {
            if( !_branchRefreshed )
            {
                foreach( var b in _git.Branches )
                {
                    if( !b.IsRemote ) _branchesFolder.Add( b );
                    else _remoteBranchesFolder.Add( b );
                }
                _branchRefreshed = true;
            }
        }

        internal IFileInfo GetFileInfo( NormalizedPath sub )
        {
            if( sub.IsEmpty ) return _thisDir;
            if( IsInWorkingFolder( ref sub ) )
            {
                if( sub.IsEmpty ) return _headFolder;
                return FileSystem.PhysicalGetFileInfo( SubPath.Combine( sub ) );
            }
            RefreshBranches();
            if( sub.FirstPart == _branchesFolder.Name )
            {
                return _branchesFolder.GetFileInfo( sub );
            }
            if( sub.FirstPart == _remoteBranchesFolder.Name )
            {
                return _remoteBranchesFolder.GetFileInfo( sub );
            }
            return null;
        }

        internal IDirectoryContents GetDirectoryContents( NormalizedPath sub )
        {
            if( sub.IsEmpty ) return _thisDir;
            if( IsInWorkingFolder( ref sub ) )
            {
                if( sub.IsEmpty ) return _headFolder;
                return FileSystem.PhysicalGetDirectoryContents( SubPath.Combine( sub ) );
            }
            RefreshBranches();
            if( sub.FirstPart == _branchesFolder.Name )
            {
                return _branchesFolder.GetDirectoryContents( sub );
            }
            if( sub.FirstPart == _remoteBranchesFolder.Name )
            {
                return _remoteBranchesFolder.GetDirectoryContents( sub );
            }
            return NotFoundDirectoryContents.Singleton;
        }

        bool IsInWorkingFolder( ref NormalizedPath sub )
        {
            if( sub.FirstPart == _headFolder.Name )
            {
                sub = sub.RemoveFirstPart();
                return true;
            }
            if( sub.Parts.Count > 1
                && (_tempBranch?.OrginalHead ?? _git.Head).FriendlyName == sub.Parts[1] )
            {
                sub = sub.RemoveParts( 0, 2 );
                return true;
            }
            return false;
        }

    }
}
