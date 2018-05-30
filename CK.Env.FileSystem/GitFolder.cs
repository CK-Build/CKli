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
    public class GitFolder
    {
        static readonly XNamespace SVGNS = XNamespace.Get( "http://csemver.org/schemas/2015" );
        static internal readonly CredentialsHandler DefaultCredentialHandler;

        readonly Repository _git;
        readonly RootDir _thisDir;
        readonly HeadFolder _headFolder;
        readonly BranchesFolder _branchesFolder;
        readonly RemotesFolder _remoteBranchesFolder;
        readonly ILocalFeedProvider _feedProvider;
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

        internal GitFolder( FileSystem fs, string gitFolder, ILocalFeedProvider blankFeedProvider, IWorldName world )
        {
            Debug.Assert( gitFolder.StartsWith( fs.Root.Path ) && gitFolder.EndsWith( ".git" ) );
            _feedProvider = blankFeedProvider;
            FullPath = new NormalizedPath( gitFolder.Remove( gitFolder.Length - 4 ) );
            SubPath = FullPath.RemovePrefix( fs.Root );
            if( SubPath.IsEmpty ) throw new InvalidOperationException( "Root path can not be a Git folder." );

            FileSystem = fs;
            World = world;
            _git = new Repository( gitFolder );
            _headFolder = new HeadFolder( this );
            _branchesFolder = new BranchesFolder( this, "branches", isRemote: false );
            _remoteBranchesFolder = new RemotesFolder( this );
            _thisDir = new RootDir( this, SubPath.LastPart );
        }

        /// <summary>
        /// Gets the current <see cref="World"/>.
        /// </summary>
        public IWorldName World { get; }

        /// <summary>
        /// Gets the full path that starts with the <see cref="FileSystem"/>' root path.
        /// </summary>
        public NormalizedPath FullPath { get; }

        /// <summary>
        /// Get the path relative to the <see cref="FileSystem"/>.
        /// </summary>
        public NormalizedPath SubPath { get; }

        /// <summary>
        /// Gets the file ssytem.
        /// </summary>
        public FileSystem FileSystem { get; }

        /// <summary>
        /// Gets the current branch name (name of the repository's HEAD).
        /// </summary>
        public string CurrentBranchName => _git.Head.FriendlyName;

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
        /// Attempts to check out a branch and pull any 'origin' changes.
        /// There must not be any uncommitted changes on the current head.
        /// The branch must exist locally or on the 'origin' remote.
        /// If the branch exists only in the "origin" remote, a local branch is automatically
        /// created that tracks the remote one.
        /// </summary>
        /// <param name="m">The monitor.</param>
        /// <param name="branchName">The local name of the branch.</param>
        /// <param name="alwaysPullAll">True to always call <see cref="PullAll"/>.</param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        public (bool Success, bool ReloadNeeded) CheckoutAndPull( IActivityMonitor m, string branchName, bool alwaysPullAll = false )
        {
            using( m.OpenInfo( $"Checking out branch '{branchName}' in '{SubPath}'." ) )
            {
                try
                {
                    if( alwaysPullAll && !PullAll( m ) ) return (false,false);
                    Branch b = _git.Branches[branchName];
                    if( b == null )
                    {
                        m.Info( $"No local branch '{branchName}' in '{SubPath}'." );
                        if( !alwaysPullAll && !PullAll( m ) ) return (false,false);
                        string remoteName = "origin/" + branchName;
                        var remote = _git.Branches[remoteName];
                        if( remote == null )
                        {
                            m.Error( $"Repository '{SubPath}': Both local '{branchName}' and remote '{remoteName}' not found." );
                            return (false,false);
                        }
                        if( !CheckCleanCommit( m ) ) return (false,false);
                        m.Info( $"Creating local branch on remote '{remoteName}'." );
                        b = _git.Branches.Add( branchName, remote.Tip );
                        b = _git.Branches.Update( b, u => u.TrackedBranch = remote.CanonicalName );
                        Commands.Checkout( _git, b );
                        return (true,true);
                    }
                    bool reloadNeeded = false;
                    if( b.IsCurrentRepositoryHead )
                    {
                        m.Trace( $"Already on {branchName}." );
                    }
                    else
                    {
                        if( !CheckCleanCommit( m ) ) return (false,false);
                        m.Info( $"Checking out {branchName} (leaving {CurrentBranchName})." );
                        Commands.Checkout( _git, b );
                        reloadNeeded = true;
                    }
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
                            CommitOnSuccess = true,
                            FailOnConflict = true,
                            FastForwardStrategy = FastForwardStrategy.Default,
                            SkipReuc = true
                        }
                    } );
                    if( result.Status == MergeStatus.Conflicts )
                    {
                        m.Error( "Merge conflicts occurred. Unable to merge changes from the remote." );
                        return (false,false);
                    }
                    return (true, reloadNeeded || result.Status != MergeStatus.UpToDate);
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return (false, false);
                }
            }
        }

        /// <summary>
        /// Fetches 'origin' (or all remotes) and merge changes into this repository.
        /// There current head must be clean.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool PullAll( IActivityMonitor m, bool originOnly = true )
        {
            using( m.OpenInfo( $"Pulling {(originOnly ? "origin" : "all remotes")} in repository '{FullPath}'" ) )
            {
                try
                {
                    if( !CheckCleanCommit( m ) ) return false;
                    var merger = _git.Config.BuildSignature( DateTimeOffset.Now );
                    var mOpt = new MergeOptions
                    {
                        CommitOnSuccess = true,
                        FailOnConflict = true,
                        FastForwardStrategy = FastForwardStrategy.Default,
                        SkipReuc = true
                    };
                    foreach( Remote remote in _git.Network.Remotes.Where( r => !originOnly || r.Name == "origin" ) )
                    {
                        m.Info( $"Fetching remote '{remote.Name}'." );
                        IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select( x => x.Specification );
                        Commands.Fetch( _git, remote.Name, refSpecs, new FetchOptions()
                        {
                            CredentialsProvider = DefaultCredentialHandler,
                            TagFetchMode = TagFetchMode.All
                        }, $"Fetching remote '{remote.Name}'." );
                        MergeResult r = _git.MergeFetchedRefs( merger, mOpt );
                        if( r.Status == MergeStatus.Conflicts )
                        {
                            m.Error( "Merge conflicts occurred. Unable to merge changes from the remote." );
                            return false;
                        }
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
        /// Gets the simple git version <see cref="RepositoryInfo"/> from a branch.
        /// Returns null if an error occurred or if RepositoryInfo.xml has not been successfully read.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Defaults to <see cref="CurrentBranchName"/>.</param>
        /// <returns>True on success, false on error.</returns>
        public RepositoryInfo ReadVersionInfo( IActivityMonitor m, string branchName = null )
        {
            if( branchName == null ) branchName = CurrentBranchName;
            try
            {
                Branch b = _git.Branches[branchName];
                if( b == null )
                {
                    m.Error( $"Unknown local branch {branchName}." );
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
                if( result.HasError )
                {
                    m.Error( result.ReleaseTagErrorText );
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
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="v">The version to set.</param>
        /// <returns>Success and whether the tag has been created or was already defined.</returns>
        public (bool Success, bool Created) SetVersionTag( IActivityMonitor m, SVersion v )
        {
            try
            {
                var exists = _git.Tags[v.ToString()];
                if( exists != null && exists.PeeledTarget == _git.Head.Tip )
                {
                    m.Info( $"Version Tag {v} is already set." );
                    return (true, false);
                }
                _git.ApplyTag( v.ToString() );
                m.Info( $"Set Version tag {v} on {CurrentBranchName}." );
                return (true, true);
            }
            catch( Exception ex )
            {
                m.Error( $"SetVersionTag {v} on {CurrentBranchName} failed.", ex );
                return (false, false);
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
            using( m.OpenInfo( $"Pushing tag {v} to remote for {SubPath}." ) )
            {
                try
                {
                    Remote remote = _git.Network.Remotes["origin"];
                    var options = new PushOptions() { CredentialsProvider = DefaultCredentialHandler };

                    var exists = _git.Tags[v.ToString()];
                    if( exists == null )
                    {
                        m.Error( $"Version Tag {v} does not exist in {SubPath}." );
                        return false;
                    }
                    _git.Network.Push( remote, exists.CanonicalName, options );
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( $"PushVersionTags failed ({v} on {SubPath}).", ex );
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
            try
            {
                _git.Tags.Remove( v.ToString() );
                m.Info( $"Removing Version tag '{v}' from {SubPath}." );
                return true;
            }
            catch( Exception ex )
            {
                m.Error( $"ClearVersionTag {v} on {SubPath} failed.", ex );
                return false;
            }
        }

        /// <summary>
        /// Gets the current commit SHA1.
        /// </summary>
        public string HeadCommitSHA1 => _git.Head.Tip.Sha;

        /// <summary>
        /// Captures <see cref="GitFolder.Commit(IActivityMonitor, string)"/> result.
        /// </summary>
        public struct CommitResult
        {
            /// <summary>
            /// True on success.
            /// </summary>
            public readonly bool Success;

            /// <summary>
            /// True if an actual commit has been created.
            /// </summary>
            public readonly bool CommitCreated;

            internal CommitResult(bool success, bool commit)
            {
                Success = success;
                CommitCreated = commit;
            }
        }

        /// <summary>
        /// Commits any pending changes.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="commitMessage">Required commit message.</param>
        /// <returns>A commit result with success/error and whether a commit has actually been created..</returns>
        public CommitResult Commit( IActivityMonitor m, string commitMessage )
        {
            if( String.IsNullOrWhiteSpace( commitMessage ) ) throw new ArgumentNullException( nameof( commitMessage ) );
            using( m.OpenInfo( $"Committing changes in '{SubPath}' (branch '{CurrentBranchName}')." ) )
            {
                try
                {
                    bool createdCommit = false;
                    Commands.Stage( _git, "*" );
                    var s = _git.RetrieveStatus();
                    if( s.IsDirty )
                    {
                        m.Info( "Working Folder is dirty. Committing changes." );
                        var now = DateTimeOffset.Now;
                        var author = _git.Config.BuildSignature( now );
                        _git.Commit( commitMessage, author, new Signature( "CKli", "none", now ) );
                        createdCommit = true;
                    }
                    else m.CloseGroup( "Working folder is up-to-date." );
                    return new CommitResult( true, createdCommit );
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return new CommitResult( false, false );
                }
            }
        }

        /// <summary>
        /// Pushes changes from a branch to the origin.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Local branch name.</param>
        /// <returns>True on success, false on error.</returns>
        public bool Push( IActivityMonitor m, string branchName )
        {
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
        /// Checkouts the <see cref="IWorldName.LocalBranchName"/>, always merging <see cref="IWorldName.DevelopBranchName"/> into it.
        /// If the repository is not on the 'local' branch, it must be on 'develop': a <see cref="Commit"/> is done to save any
        /// current work, the 'local' branch is created if needed and checked out.
        /// 'develop' branch is always merged into it.
        /// If the the merge fails, a manual operation is required.
        /// On success, RepositoryInfo.xml and nuget.config are modified to work on LocalFeed/Blank.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchFromDevelopToLocal( IActivityMonitor m, bool autoCommit = true )
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
                            if( !Commit( m, $"Switching to {World.LocalBranchName} branch." ).Success ) return false;
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
                        Commands.Checkout( _git, local );
                    }
                    else m.Trace( $"Already on {World.LocalBranchName}." );

                    var merger = _git.Config.BuildSignature( DateTimeOffset.Now );
                    var r = _git.Merge( develop, merger, new MergeOptions
                    {
                        FastForwardStrategy = FastForwardStrategy.NoFastForward,
                        CommitOnSuccess = true,
                        FailOnConflict = true
                    } );
                    if( r.Status == MergeStatus.Conflicts )
                    {
                        m.Error( $"Merge failed from '{World.DevelopBranchName}' to '{World.LocalBranchName}': conflicts must be manually resolved." );
                        return false;
                    }

                    var localStorePath = _feedProvider.GetLocalCKSetupStorePath( m );
                    if( !EnsureCKSetupStoreTestHelperConfig( m, localStorePath ) ) return false;
                    if( !EnsureRepositoryXmlBlankDevBranch( m ) ) return false;
                    if( !EnsureLocalFeedsNuGetSource( m ).Success ) return false;
                    if( !Commit( m, "Updated Repository.xml and nuget.config for 'local' branch." ).Success ) return false;
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
        /// Checkouts the <see cref="IWorldName.MasterBranchName"/>, always merging <see cref="IWorldName.DevelopBranchName"/> into it.
        /// If the repository is not already on the 'master' branch, it must be on 'develop' and on a clean commit.
        /// The 'master' branch is created if needed and checked out.
        /// 'develop' branch is always merged into it.
        /// If the the merge fails, a manual operation is required.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchFromDevelopToMaster( IActivityMonitor m )
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
                        Commands.Checkout( _git, master );
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
        /// Checkouts '<see cref="IWorldName.DevelopBranchName"/>' branch and merges current 'local' in it.
        /// A CI-Build of the whole stack should be executed with the nuget.config file containing the 'LocalFeed' source. 
        /// followed by an update of BuildProjects package references.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchFromLocalToDevelop( IActivityMonitor m )
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
                    if( !RemoveCKSetupStoreTestHelperConfig( m ) ) return false;
                    if( !RemoveRepositoryXmlBlankDevBranch( m ) ) return false;
                    if( bLocal.IsCurrentRepositoryHead )
                    {
                        if( !Commit( m, $"Switching to '{World.DevelopBranchName}' branch." ).Success ) return false;
                        Commands.Checkout( _git, bDevelop );
                    }
                    else
                    {
                        if( !Commit( m, $"Auto commit on '{World.DevelopBranchName}' branch." ).Success ) return false;
                    }

                    var merger = _git.Config.BuildSignature( DateTimeOffset.Now );
                    var r = _git.Merge( bLocal, merger, new MergeOptions
                    {
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
        public bool SwitchFromMasterToDevelop( IActivityMonitor m )
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
                        Commands.Checkout( _git, develop );
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

        (XDocument Doc, string Path) GetXmlDocument( IActivityMonitor m, string fileName )
        {
            var pathXml = SubPath.AppendPart( "branches" ).AppendPart( CurrentBranchName ).AppendPart( fileName );
            var rXml = FileSystem.GetFileInfo( pathXml );
            if( !rXml.Exists || rXml.IsDirectory || rXml.PhysicalPath == null )
            {
                m.Fatal( $"{pathXml} must exist." );
                return (null, null);
            }
            return (rXml.ReadAsXDocument(), pathXml);
        }

        (XDocument Doc, string Path, XElement PackageSources) GetNuGetConfigFile( IActivityMonitor m )
        {
            var (xDoc, pathXml) = GetXmlDocument( m, "nuget.config" );
            if( xDoc == null ) return (null, null, null );

            var e = xDoc.Root;
            var packageSources = e.Element( "packageSources" );
            if( packageSources == null )
            {
                m.Fatal( $"nuget.config must contain at least one <packageSources> element." );
                return (null, null, null);
            }
            return (xDoc, pathXml, packageSources);
        }

        public (bool Success, bool Added) EnsureLocalFeedsNuGetSource( IActivityMonitor m, bool ensureRelease = true, bool ensureCI = true, bool ensureLocal = true )
        {
            var (xDoc, pathXml, packageSources) = GetNuGetConfigFile( m );
            if( xDoc == null ) return (false,false);
            bool added = false;
            if( ensureRelease && !packageSources.Elements( "add" ).Any( x => (string)x.Attribute( "key" ) == "LocalFeed-Release" ) )
            {
                var localFeed = _feedProvider.GetReleaseFeedFolder( m ).PhysicalPath;
                packageSources.Add( new XElement( "add",
                                                new XAttribute( "key", "LocalFeed-Release" ),
                                                new XAttribute( "value", localFeed ) ) );
                added = true;
            }
            if( ensureCI && !packageSources.Elements( "add" ).Any( x => (string)x.Attribute( "key" ) == "LocalFeed-CI" ) )
            {
                var localFeed = _feedProvider.GetCIFeedFolder( m ).PhysicalPath;
                packageSources.Add( new XElement( "add",
                                                new XAttribute( "key", "LocalFeed-CI" ),
                                                new XAttribute( "value", localFeed ) ) );
                added = true;
            }
            if( ensureLocal && !packageSources.Elements( "add" ).Any( x => (string)x.Attribute( "key" ) == "LocalFeed-Local" ) )
            {
                var blankFeed = _feedProvider.GetLocalFeedFolder( m ).PhysicalPath;
                packageSources.Add( new XElement( "add",
                                                new XAttribute( "key", "LocalFeed-Local" ),
                                                new XAttribute( "value", blankFeed ) ) );
                added = true;
            }
            if( added )
            {
                return (FileSystem.CopyTo( m, xDoc.ToString(), pathXml ), true );
            }
            return (true, false);
        }

        public (bool Success, bool Removed) RemoveLocalFeedsNuGetSource( IActivityMonitor m )
        {
            var (xDoc, pathXml, packageSources) = GetNuGetConfigFile( m );
            if( xDoc == null ) return (false,false);
            var e = packageSources
                     .Elements( "add" )
                     .Where( b => (string)b.Attribute( "key" ) == "LocalFeed-Local"
                                    || (string)b.Attribute( "key" ) == "LocalFeed-CI"
                                    || (string)b.Attribute( "key" ) == "LocalFeed-Release" );
            if( e.Any() )
            {
                e.Remove();
                return (FileSystem.CopyTo( m, xDoc.ToString(), pathXml ), true);
            }
            return (true,false);
        }

        public bool SetRepositoryXmlIgnoreDirtyFolders( IActivityMonitor m )
        {
            var (xDoc, pathXml) = GetXmlDocument( m, "RepositoryInfo.xml" );
            if( xDoc == null ) return false;
            var e = xDoc.Root;
            var debug = e.Element( SVGNS + "Debug" );
            if( debug == null ) e.Add( debug = new XElement( SVGNS + "Debug" ) );
            if( (string)debug.Attribute( "IgnoreDirtyWorkingFolder" ) != "true" )
            {
                debug.SetAttributeValue( "IgnoreDirtyWorkingFolder", "true" );
            }
            return FileSystem.CopyTo( m, xDoc.ToString(), pathXml );
        }

        bool EnsureRepositoryXmlBlankDevBranch( IActivityMonitor m )
        {
            var (xDoc, pathXml) = GetXmlDocument( m, "RepositoryInfo.xml" );
            if( xDoc == null ) return false;
            var e = xDoc.Root;
            var branches = e.Element( SVGNS + "Branches" );
            if( branches == null ) e.Add( branches = new XElement( SVGNS + "Branches" ) );

            var branch = branches.Elements( SVGNS + "Branch" )
                                 .Where( b => (string)b.Attribute( "Name" ) == World.LocalBranchName );
            if( !branch.Any() )
            {
                branches.Add( new XElement( SVGNS + "Branch",
                                   new XAttribute( "Name", World.LocalBranchName ),
                                   new XAttribute( "VersionName", "local" ),
                                   new XAttribute( "CIVersionMode", "LastReleaseBased" ) ) );
            }
            else
            {
                var b = branch.First();
                b.SetAttributeValue( "VersionName", "local" );
                b.SetAttributeValue( "CIVersionMode", "LastReleaseBased" );
            }
            return FileSystem.CopyTo( m, xDoc.ToString(), pathXml );
        }

        bool RemoveRepositoryXmlBlankDevBranch( IActivityMonitor m )
        {
            var (xDoc, pathXml) = GetXmlDocument( m, "RepositoryInfo.xml" );
            if( xDoc == null ) return false;
            xDoc.Root.Element( SVGNS + "Branches" )
                     .Elements( SVGNS + "Branch" )
                     .Where( b => (string)b.Attribute( "Name" ) == World.LocalBranchName )
                     .Remove();
            return FileSystem.CopyTo( m, xDoc.ToString(), pathXml );
        }

        public bool EnsureCKSetupStoreTestHelperConfig( IActivityMonitor m, string storePath )
        {
            var path = SubPath.AppendPart( "branches" ).AppendPart( CurrentBranchName ).AppendPart( "RemoteStore.TestHelper.config" );
            var f = FileSystem.GetFileInfo( path );
            if( f.IsDirectory )
            {
                m.Fatal( $"{path} exists as a directory." );
                return false;
            }
            if( !f.Exists )
            {
                var text = "<configuration><appSettings>" + Environment.NewLine;
                text += "  -- This forces the Solutions that generate components to use the LocalFeed/Local/CKSetupStore" + Environment.NewLine;;
                text += $@"  <add key=""CKSetup/DefaultStoreUrl"" value=""{storePath}"" />" + Environment.NewLine;
                text += $@"  <add key=""CKSetup/DefaultStorePath"" value=""{storePath}"" />" + Environment.NewLine;
                text += "</appSettings></configuration>";
                if( !FileSystem.CopyTo( m, text, path ) ) return false;
            }
            return true;
        }

        public bool RemoveCKSetupStoreTestHelperConfig( IActivityMonitor m )
        {
            var path = SubPath.AppendPart( "branches" ).AppendPart( CurrentBranchName ).AppendPart( "RemoteStore.TestHelper.config" );
            return FileSystem.Delete( m, path );
        }

        internal void Dispose()
        {
            _git.Dispose();
        }

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

            public override DateTimeOffset LastModified => Directory.GetLastWriteTimeUtc( _f.FullPath.Path );

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

            public override string PhysicalPath => _f.FullPath.Path;

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
            if( sub.Parts.Count > 1 && _git.Head.FriendlyName == sub.Parts[1] )
            {
                sub = sub.RemoveParts( 0, 2 );
                return true;
            }
            return false;
        }
    }
}
