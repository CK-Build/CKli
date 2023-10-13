using CK.Core;

using CSemVer;
using LibGit2Sharp;
using Microsoft.Extensions.FileProviders;
using SimpleGitVersion;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Implements Git repository mapping.
    /// GitFolder are internally created (and disposed) by <see cref="FileSystem"/>.
    /// </summary>
    public partial class GitRepository : GitRepositoryBase, IGitHeadInfo, ICommandMethodsProvider
    {
        readonly RootDir _thisDir;
        readonly HeadFolder _headFolder;
        readonly BranchesFolder _branchesFolder;
        readonly RemotesFolder _remoteBranchesFolder;
        bool _branchRefreshed;

        /// <summary>
        /// Gets the <see cref="ProtoGitFolder"/>.
        /// </summary>
        public ProtoGitFolder ProtoGitFolder => (ProtoGitFolder)RepositoryKey;

        internal GitRepository( Repository r, ProtoGitFolder data )
            : base( data, r, data.FullPhysicalPath, data.FolderPath )
        {
            _headFolder = new HeadFolder( this );
            _branchesFolder = new BranchesFolder( this, "branches", isRemote: false );
            _remoteBranchesFolder = new RemotesFolder( this );
            _thisDir = new RootDir( this, DisplayPath.LastPart );
            ServiceContainer = new SimpleServiceContainer( FileSystem.ServiceContainer );
            ServiceContainer.Add( this );
            PluginManager = new GitPluginManager( data.PluginRegistry, ServiceContainer, FileSystem.CommandRegister, data.World.DevelopBranchName );
            FileSystem.CommandRegister.Register( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => DisplayPath;

        /// <summary>
        /// Gets the service container for this GitFolder.
        /// </summary>
        public SimpleServiceContainer ServiceContainer { get; }

        /// <summary>
        /// Gets the plugin manager for this GitFolder and its branches.
        /// </summary>
        public GitPluginManager PluginManager { get; }

        /// <summary>
        /// Gets a <see cref="GitRepositoryBase.SimpleStatusInfo"/> for this repository.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="initializePlugins">True to initialize the plugins if they are not initialized.</param>
        /// <returns>The simplified status info.</returns>
        public SimpleStatusInfo GetSimpleStatusInfo( IActivityMonitor monitor, bool initializePlugins )
        {
            var r = GetSimpleStatusInfo();
            if( PluginManager.BranchPlugins.IsInitialized( CurrentBranchName )
                || (initializePlugins && EnsureCurrentBranchPlugins( monitor )) )
            {
                r.PluginCount = PluginManager.BranchPlugins.Count;
            }
            return r;
        }

        /// <summary>
        /// Ensures that plugins are loaded for the <see cref="CurrentBranchName"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns></returns>
        public bool EnsureCurrentBranchPlugins( IActivityMonitor monitor )
        {
            Throw.CheckState( CurrentBranchName != null );
            return PluginManager.BranchPlugins.EnsurePlugins( monitor, CurrentBranchName );
        }

        protected override void OnNewCurrentBranch( IActivityMonitor m ) => EnsureCurrentBranchPlugins( m );

        /// <summary>
        /// Fires whenever we switched to the local branch.
        /// </summary>
        public event EventHandler<EventMonitoredArgs>? OnLocalBranchEntered;

        /// <summary>
        /// Fires whenever we are up to leave the local branch back to the develop one.
        /// </summary>
        public event EventHandler<EventMonitoredArgs>? OnLocalBranchLeaving;

        /// <summary>
        /// Fires whenever the repository is reset.
        /// </summary>
        public event Action<IActivityMonitor>? OnReset;


        /// <summary>
        /// Gets the standard git status, based on the <see cref="CurrentBranchName"/>.
        /// </summary>
        public StandardGitStatus StandardGitStatus => CurrentBranchName == World.LocalBranchName
                                                        ? StandardGitStatus.Local
                                                        : (CurrentBranchName == World.DevelopBranchName
                                                            ? StandardGitStatus.Develop
                                                            : StandardGitStatus.Unknown);
        public IWorldName World => ProtoGitFolder.World;

        /// <summary>
        /// Event raised when <see cref="RunProcess(IActivityMonitor, string, string)"/> is called.
        /// </summary>
        public event EventHandler<RunCommandEventArgs>? RunProcessStarting;

        /// <summary>
        /// Runs a command at the root of this repository.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="fileName">The filename to execute.</param>
        /// <param name="arguments">the raw string of arguments.</param>
        /// <returns>True on success. False on error.</returns>
        [CommandMethod]
        public bool RunProcess( IActivityMonitor m, string fileName, string arguments, int timeoutSeconds = 60 )
        {
            ProcessStartInfo info = ProcessRunner.ConfigureProcessInfo( FullPhysicalPath, fileName, arguments );
            RunCommandEventArgs arg = new RunCommandEventArgs( m, info );
            RunProcessStarting?.Invoke( this, arg );
            return ProcessRunner.Run( m, arg.StartInfo, timeoutSeconds * 1000, arg.StdErrorLevel );
        }

        class AdaptedLogger : ILogger
        {
            readonly IActivityMonitor _m;
            public AdaptedLogger( IActivityMonitor m ) => _m = m;
            public void Error( string msg ) => _m.Error( msg );
            public void Warn( string msg ) => _m.Warn( msg );
            public void Info( string msg ) => _m.Info( msg );
        }

        /// <summary>
        /// Gets the simple git version <see cref="ICommitInfo"/> from a branch.
        /// Returns null if an error occurred or if RepositoryInfo.xml has not been successfully read.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="branchName">Defaults to <see cref="CurrentBranchName"/>.</param>
        /// <returns>The RepositoryInfo with the SingleMajor and OnlyPatch configured options or null if it cannot be obtained.</returns>
        public (ICommitInfo Commit, int? SingleMajor, bool OnlyPatch)? ReadVersionInfo( IActivityMonitor m, string? branchName = null )
        {
            if( branchName == null ) branchName = CurrentBranchName;
            try
            {
                using( m.OpenInfo( $"Reading version info of '{DisplayPath}/{branchName}'." ) )
                {
                    Branch b = Git.Branches[branchName];
                    if( b == null )
                    {
                        m.Error( $"Unknown branch {branchName}." );
                        return null;
                    }
                    var pathOpt = b.IsRemote
                                    ? DisplayPath.AppendPart( "remotes" ).Combine( b.FriendlyName )
                                    : DisplayPath.AppendPart( "branches" ).AppendPart( branchName );

                    pathOpt = pathOpt.AppendPart( "RepositoryInfo.xml" );
                    var fOpt = FileSystem.GetFileInfo( pathOpt );
                    if( !fOpt.Exists )
                    {
                        m.Error( $"Missing required {pathOpt} file." );
                        return null;
                    }
                    var oRoot = fOpt.ReadAsXDocument().Root;
                    if( oRoot == null )
                    {
                        m.Error( $"Missing Xml Root element in '{pathOpt}' file." );
                        return null;
                    }
                    var opt = new RepositoryInfoOptions( oRoot ) { HeadBranchName = branchName, IgnoreAlreadyExistingVersion = true };
                    var result = new CommitInfo( Git, opt );
                    result.Explain( new AdaptedLogger( m ) );
                    return result.Error == null ? (result,opt.SingleMajor,opt.OnlyPatch) : null;
                }
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
                var exists = Git.Tags[sv];
                if( exists != null && exists.PeeledTarget == Git.Head.Tip )
                {
                    m.Info( $"Version Tag {sv} is already set." );
                    return true;
                }
                Git.ApplyTag( sv );
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
            using( m.OpenInfo( $"Pushing tag {sv} to remote for {DisplayPath}." ) )
            {
                try
                {
                    Remote remote = Git.Network.Remotes["origin"];
                    var options = new PushOptions()
                    {
                        CredentialsProvider = ( url, user, cred ) => ProtoGitFolder.PATCredentialsHandler( m )
                    };

                    var exists = Git.Tags[sv];
                    if( exists == null )
                    {
                        m.Error( $"Version Tag {sv} does not exist in {DisplayPath}." );
                        return false;
                    }
                    Git.Network.Push( remote, exists.CanonicalName, options );
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( $"PushVersionTags failed ({sv} on {DisplayPath}).", ex );
                    return false;
                }
            }
        }

        /// <summary>
        /// Removes a version lightweight tag from the repository.
        /// If the tag 
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="v">The version to remove.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ClearVersionTag( IActivityMonitor m, SVersion v )
        {
            var sv = 'v' + v.ToString();
            try
            {
                if( Git.Tags[sv] == null )
                {
                    m.Info( $"Tag '{sv}' in {DisplayPath} not found (cannot remove it)." );
                }
                else
                {
                    Git.Tags.Remove( sv );
                    m.Info( $"Removing Version tag '{sv}' from {DisplayPath}." );
                }
                return true;
            }
            catch( Exception ex )
            {
                m.Error( $"ClearVersionTag {sv} on {DisplayPath} failed.", ex );
                return false;
            }
        }

        public FileSystem FileSystem => ProtoGitFolder.FileSystem;

        /// <summary>
        /// Pulls current branch by merging changes from remote 'origin' branch into this repository.
        /// The current head must be clean.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>
        /// Success is true on success, false on error (such as merge conflicts) and in case of success,
        /// the result states whether a reload should be required or if nothing changed.
        /// </returns>
        public (bool Success, bool ReloadNeeded) Pull( IActivityMonitor m ) => Pull( m, MergeFileFavor.Theirs );

        /// <summary>
        /// Raises the <see cref="OnReset"/> event only or calls <see cref="ResetHard(IActivityMonitor)"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="gitResetHard">True to reset the working folder.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool Reset( IActivityMonitor monitor, bool gitResetHard = false )
        {
            if( gitResetHard ) return ResetHard( monitor );
            using( monitor.OpenInfo( $"Soft reset '{DisplayPath}' (branch '{CurrentBranchName}')." ) )
            {
                OnReset?.Invoke( monitor );
                return true;
            }
        }

        /// <summary>
        /// Resets the index to the tree recorded by the commit and updates the working directory to
        /// match the content of the index and raises the <see cref="OnReset"/> event.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool ResetHard( IActivityMonitor monitor )
        {
            using( monitor.OpenInfo( $"Reset --hard changes in '{DisplayPath}' (branch '{CurrentBranchName}')." ) )
            {
                try
                {
                    Git.Reset( ResetMode.Hard );
                    var status = Git.RetrieveStatus( _checkDirtyOptions );
                    int untrackedCount = status.Untracked.Count();
                    bool success = true;
                    if( untrackedCount > 0 )
                    {
                        using( monitor.OpenWarn( $"Attempting to delete {untrackedCount} untracked files." ) )
                        {
                            foreach( var e in status.Untracked )
                            {
                                if( !FileSystem.Delete( monitor, DisplayPath.AppendPart( "branches" ).AppendPart( CurrentBranchName ).Combine( e.FilePath ) ) )
                                {
                                    success = false;
                                }
                            }
                        }
                    }
                    return success;
                }
                catch( Exception ex )
                {
                    monitor.Error( ex );
                    return false;
                }
                finally
                {
                    OnReset?.Invoke( monitor );
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
            Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
            bool delete = String.IsNullOrWhiteSpace( commitSha );
            using( m.OpenInfo( delete
                                ? $"Restoring {DisplayPath} '{branchName}' state (removing it)."
                                : $"Restoring {DisplayPath} '{branchName}' state." ) )
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
                        var toDelete = Git.Branches[branchName];
                        if( toDelete == null )
                        {
                            m.Info( $"Branch '{branchName}' does not exist." );
                            return true;
                        }
                        Git.Branches.Remove( toDelete );
                        m.Info( $"Branch '{branchName}' has been removed." );
                        return true;
                    }
                    Debug.Assert( !delete );
                    var current = Git.Head;
                    if( branchName == current.FriendlyName )
                    {
                        if( commitSha == current.Tip.Sha )
                        {
                            m.Info( $"Current branch '{current}' is already on restored state." );
                            return true;
                        }
                        Git.Reset( ResetMode.Hard, commitSha );
                        m.Info( $"Current branch '{current}' has been restored to {commitSha}." );
                        return true;
                    }
                    var b = Git.Branches[branchName];
                    if( b == null )
                    {
                        m.Warn( $"Branch '{branchName}' not found." );
                        return true;
                    }
                    if( commitSha == b.Tip.Sha )
                    {
                        m.Info( $"Branch '{branchName}' is already on restored state." );
                        return true;
                    }
                    Commands.Checkout( Git, b );
                    Git.Reset( ResetMode.Hard, commitSha );
                    m.Info( $"Branch '{branchName}' has been restored to {commitSha}." );
                    Commands.Checkout( Git, current );
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
        /// 'develop' branch is always merged into it, preferring file modifications from the 'develop' branch.
        /// If the merge fails, a manual operation is required.
        /// On success, the solution inside should be purely local: there should not be any possible remote interactions (except
        /// possibly importing fully external packages).
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="autoCommit">False to require the working folder to be clean and not automatically creating a commit.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchDevelopToLocal( IActivityMonitor monitor, bool autoCommit = true )
        {
            using( monitor.OpenInfo( $"Switching '{DisplayPath}' to branch '{World.LocalBranchName}')." ) )
            {
                try
                {
                    Branch develop = Git.Branches[World.DevelopBranchName];
                    if( develop == null )
                    {
                        monitor.Error( $"Unable to find branch '{World.DevelopBranchName}'." );
                        return false;
                    }
                    // Auto merge from Ours or Theirs: we favor the current branch.
                    MergeFileFavor mergeFileFavor = MergeFileFavor.Normal;
                    Branch local = Git.Branches[World.LocalBranchName];
                    if( local == null || !local.IsCurrentRepositoryHead )
                    {
                        if( !develop.IsCurrentRepositoryHead )
                        {
                            monitor.Error( $"Expected current branch to be '{World.DevelopBranchName}' or '{World.LocalBranchName}'." );
                            return false;
                        }
                        if( autoCommit )
                        {
                            if( Commit( monitor, $"Switching to {World.LocalBranchName} branch." ) == CommittingResult.Error ) return false;
                        }
                        else
                        {
                            if( !CheckCleanCommit( monitor ) ) return false;
                        }
                        if( local == null )
                        {
                            monitor.Info( $"Creating the {World.LocalBranchName}." );
                            local = Git.CreateBranch( World.LocalBranchName );
                        }
                        else
                        {
                            monitor.Info( "Coming from develop: favors 'develop' file changes during merge." );
                            mergeFileFavor = MergeFileFavor.Theirs;
                        }
                        Commands.Checkout( Git, local );
                        OnNewCurrentBranch( monitor );
                    }
                    else
                    {
                        monitor.Info( $"Already on {World.LocalBranchName}: favors 'local' file changes during merge." );
                        mergeFileFavor = MergeFileFavor.Ours;
                        EnsureCurrentBranchPlugins( monitor );
                    }
                    var merger = Git.Config.BuildSignature( DateTimeOffset.Now );
                    var r = Git.Merge( develop, merger, new MergeOptions
                    {
                        MergeFileFavor = mergeFileFavor,
                        FastForwardStrategy = FastForwardStrategy.NoFastForward,
                        CommitOnSuccess = true,
                        FailOnConflict = true
                    } );
                    if( r.Status == MergeStatus.Conflicts )
                    {
                        monitor.Error( $"Merge failed from '{World.DevelopBranchName}' to '{World.LocalBranchName}': conflicts must be manually resolved." );
                        return false;
                    }

                    if( !RaiseEnteredLocalBranch( monitor, true ) ) return false;

                    if( AmendCommit( monitor ) == CommittingResult.Error ) return false;
                    if( r.Status != MergeStatus.UpToDate )
                    {
                        monitor.CloseGroup( $"Success (with merge from '{World.DevelopBranchName}')." );
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

        bool RaiseEnteredLocalBranch( IActivityMonitor m, bool enter )
        {
            if( !PluginManager.BranchPlugins.EnsurePlugins( m, World.LocalBranchName ) ) return false;
            using( m.OpenTrace( $"{this}: Raising {(enter ? "OnLocalBranchEntered" : "OnLocalBranchLeaving")} event." ) )
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
        /// If the repository is not already on the <see cref="IWorldName.MasterBranchName"/> branch, it must be on 'develop' and on a clean commit.
        /// The <see cref="WorldName.MasterBranchName"/> branch is created if needed and checked out.
        /// 'develop' branch is always merged into it.
        /// If the merge fails, a manual operation is required.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchDevelopToMaster( IActivityMonitor monitor )
        {
            using( monitor.OpenInfo( $"Switching '{DisplayPath}' to branch '{World.MasterBranchName}')." ) )
            {
                try
                {
                    Branch bDevelop = Git.Branches[World.DevelopBranchName];
                    if( bDevelop == null )
                    {
                        monitor.Error( $"Unable to find branch '{World.DevelopBranchName}'." );
                        return false;
                    }
                    Branch master = Git.Branches[World.MasterBranchName];
                    if( master == null || !master.IsCurrentRepositoryHead )
                    {
                        if( !bDevelop.IsCurrentRepositoryHead )
                        {
                            monitor.Error( $"Expected current branch to be '{World.DevelopBranchName}' or '{World.MasterBranchName}'." );
                            return false;
                        }
                        if( !CheckCleanCommit( monitor ) ) return false;
                        if( master == null )
                        {
                            monitor.Info( $"Creating the {World.MasterBranchName }." );
                            master = Git.CreateBranch( World.MasterBranchName );
                        }
                        Commands.Checkout( Git, master );
                        OnNewCurrentBranch( monitor );
                    }
                    else
                    {
                        monitor.Trace( $"Already on {World.MasterBranchName}." );
                    }

                    var merger = Git.Config.BuildSignature( DateTimeOffset.Now );
                    var r = Git.Merge( bDevelop, merger, new MergeOptions
                    {
                        FastForwardStrategy = FastForwardStrategy.NoFastForward,
                        CommitOnSuccess = true,
                        FailOnConflict = true
                    } );
                    if( r.Status == MergeStatus.Conflicts )
                    {
                        monitor.Error( $"Merge failed from '{World.DevelopBranchName}' to '{World.MasterBranchName}': conflicts must be manually resolved." );
                        return false;
                    }
                    if( r.Status != MergeStatus.UpToDate )
                    {
                        monitor.CloseGroup( $"Success (with merge from '{World.DevelopBranchName}')." );
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
        /// Checkouts '<see cref="IWorldName.DevelopBranchName"/>' branch (that must be clean)
        /// and merges current 'local' in it.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchLocalToDevelop( IActivityMonitor monitor )
        {
            using( monitor.OpenInfo( $"Switching '{DisplayPath}' to branch '{World.DevelopBranchName}'." ) )
            {
                try
                {
                    Branch bDevelop = Git.Branches[World.DevelopBranchName];
                    if( bDevelop == null )
                    {
                        monitor.Error( $"Unable to find '{World.DevelopBranchName}' branch." );
                        return false;
                    }
                    Branch bLocal = Git.Branches[World.LocalBranchName];
                    if( bLocal == null )
                    {
                        monitor.Error( $"Unable to find '{World.LocalBranchName}' branch." );
                        return false;
                    }
                    if( !bDevelop.IsCurrentRepositoryHead && !bLocal.IsCurrentRepositoryHead )
                    {
                        monitor.Error( $"Must be on '{World.LocalBranchName}' or '{World.DevelopBranchName}' branch." );
                        return false;
                    }

                    // Auto merge from Ours or Theirs: we favor the current branch.
                    MergeFileFavor mergeFileFavor = MergeFileFavor.Normal;
                    if( bLocal.IsCurrentRepositoryHead )
                    {
                        if( !RaiseEnteredLocalBranch( monitor, false ) ) return false;
                        if( AmendCommit( monitor ) == CommittingResult.Error ) return false;
                        Commands.Checkout( Git, bDevelop );
                        OnNewCurrentBranch( monitor );
                        monitor.Info( $"Coming from {World.LocalBranchName}: favors 'local' file changes during merge." );
                        mergeFileFavor = MergeFileFavor.Theirs;
                    }
                    else
                    {
                        if( !CheckCleanCommit( monitor ) ) return false;
                        EnsureCurrentBranchPlugins( monitor );
                        monitor.Info( $"Already on {World.DevelopBranchName}: favors 'develop' file changes during merge." );
                        mergeFileFavor = MergeFileFavor.Ours;
                    }

                    var merger = Git.Config.BuildSignature( DateTimeOffset.Now );
                    var r = Git.Merge( bLocal, merger, new MergeOptions
                    {
                        MergeFileFavor = mergeFileFavor,
                        FastForwardStrategy = FastForwardStrategy.NoFastForward,
                        FailOnConflict = true,
                        CommitOnSuccess = true
                    } );
                    if( r.Status == MergeStatus.Conflicts )
                    {
                        monitor.Error( $"Merge failed from '{World.LocalBranchName}' into '{World.DevelopBranchName}': conflicts must be manually resolved." );
                        return false;
                    }
                    if( r.Status != MergeStatus.UpToDate )
                    {
                        monitor.CloseGroup( $"Success (with merge from '{World.LocalBranchName}')." );
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
        /// Simple safe check out of the <see cref="IWorldName.DevelopBranchName"/> (that must exist) from
        /// the <see cref="IWorldName.MasterBranchName"/> (that may not exist).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        public bool SwitchMasterToDevelop( IActivityMonitor m )
        {
            using( m.OpenInfo( $"Switching '{DisplayPath}' to branch '{World.DevelopBranchName}'." ) )
            {
                try
                {
                    Branch develop = Git.Branches[World.DevelopBranchName];
                    if( develop == null )
                    {
                        m.Error( $"Unable to find '{World.DevelopBranchName}' branch." );
                        return false;
                    }
                    Branch master = Git.Branches[World.MasterBranchName];
                    if( !develop.IsCurrentRepositoryHead && (master == null || !master.IsCurrentRepositoryHead) )
                    {
                        m.Error( $"Must be on '{World.MasterBranchName}' or '{World.DevelopBranchName}' branch." );
                        return false;
                    }
                    if( master != null && master.IsCurrentRepositoryHead )
                    {
                        if( !CheckCleanCommit( m ) ) return false;
                        Commands.Checkout( Git, develop );
                        OnNewCurrentBranch( m );
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

        public override void Dispose()
        {
            FileSystem.CommandRegister.Unregister( this );
            ((IDisposable)PluginManager).Dispose();
            base.Dispose();
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

            public virtual string? PhysicalPath => null;

            Stream IFileInfo.CreateReadStream()
            {
                throw new InvalidOperationException( "Cannot create a stream for a directory." );
            }

            public abstract DateTimeOffset LastModified { get; }

        }

        /// <summary>
        /// Root directory is this GitRepository.
        /// Its content is "head" (<see cref="HeadFolder"/>), "branches" (<see cref="BranchesFolder"/>)
        /// and "remotes" (<see cref="RemotesFolder"/>).
        /// <para>
        /// This doesn't override <see cref="BaseDirFileInfo.PhysicalPath"/> since this is the physical
        /// path of <see cref="HeadFolder"/>: this one is purely virtual.
        /// </para>
        /// </summary>
        sealed class RootDir : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitRepository _repository;
            readonly IReadOnlyList<IFileInfo> _content;

            internal RootDir( GitRepository r, string name )
                : base( name )
            {
                _repository = r;
                _content = new IFileInfo[] { r._headFolder, r._branchesFolder, r._remoteBranchesFolder };
            }

            public override DateTimeOffset LastModified => Directory.GetLastWriteTimeUtc( _repository.FullPhysicalPath.Path );

            public IEnumerator<IFileInfo> GetEnumerator() => _content.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        }

        /// <summary>
        /// "head" sub folder. Its <see cref="BaseDirFileInfo.PhysicalPath"/> is the GitRepository path.
        /// </summary>
        sealed class HeadFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitRepository _f;
            IDirectoryContents? _physical;

            public HeadFolder( GitRepository f )
                : base( "head" )
            {
                _f = f;
            }

            public override DateTimeOffset LastModified => _f.Git.Head.Tip.Committer.When;

            public override string? PhysicalPath => _f.FullPhysicalPath.Path;

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                _physical ??= _f.FileSystem.PhysicalGetDirectoryContents( _f.DisplayPath );
                return _physical.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// RemotesFolder (named "remotes") are children of the <see cref="RootDir"/> and
        /// contain named <see cref="BranchesFolder"/>.
        /// </summary>
        sealed class RemotesFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitRepository _f;
            readonly SortedDictionary<string, BranchesFolder> _origins;

            public RemotesFolder( GitRepository f )
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

            public IFileInfo? GetFileInfo( NormalizedPath sub )
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

        /// <summary>
        /// BranchesFolder are named. One is "branches" that is a child of the <see cref="RootDir"/>
        /// and others that are children of <see cref="RemotesFolder"/>.
        /// They hold named <see cref="CommitFolder"/>.
        /// </summary>
        sealed class BranchesFolder : BaseDirFileInfo, IDirectoryContents
        {
            readonly GitRepository _f;
            readonly SortedDictionary<string, CommitFolder> _branches;
            readonly bool _isRemote;

            public BranchesFolder( GitRepository f, string name, bool isRemote )
                : base( name )
            {
                _f = f;
                _branches = new SortedDictionary<string, CommitFolder>();
                _isRemote = isRemote;
            }

            public override DateTimeOffset LastModified => _f.Git.Head.Tip.Committer.When;

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
                else
                {
                    _branches.Add( name, new CommitFolder( name, b.Tip ) );
                }

                return true;
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                _f.RefreshBranches();
                return _branches.Values.GetEnumerator();
            }

            public IFileInfo? GetFileInfo( NormalizedPath sub )
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

        /// <summary>
        /// Wraps a Commit. CommitFolder are named children of a <see cref="BranchesFolder"/>.
        /// </summary>
        sealed class CommitFolder : BaseDirFileInfo, IDirectoryContents
        {
            public readonly Commit Commit;

            public CommitFolder( string name, Commit c )
                : base( name )
            {
                Commit = c;
            }

            public override DateTimeOffset LastModified => Commit.Committer.When;

            public IFileInfo? GetFileInfo( NormalizedPath sub )
            {
                var e = Commit.Tree[sub];
                if( e != null && e.TargetType != TreeEntryTargetType.GitLink )
                {
                    return new TreeEntryWrapper( e, this );
                }
                return null;
            }

            public IDirectoryContents GetDirectoryContents( NormalizedPath sub )
            {
                if( sub.IsEmptyPath ) return this;
                TreeEntry e = Commit.Tree[sub];
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

        /// <summary>
        /// Wraps a <see cref="CommitFolder"/> and a LibGit's TreeEntry.
        /// </summary>
        sealed class TreeEntryWrapper : IFileInfo, IDirectoryContents
        {
            readonly TreeEntry _e;
            readonly CommitFolder _c;

            public TreeEntryWrapper( TreeEntry e, CommitFolder c )
            {
                Debug.Assert( e.TargetType == TreeEntryTargetType.Blob || e.TargetType == TreeEntryTargetType.Tree );
                _e = e;
                _c = c;
            }

            Blob? Blob => _e.Target as Blob;

            public bool Exists => true;

            public long Length => Blob?.Size ?? -1;

            public string? PhysicalPath => null;

            public string Name => _e.Name;

            public DateTimeOffset LastModified => _c.LastModified;

            public bool IsDirectory => _e.TargetType == TreeEntryTargetType.Tree;

            public Stream CreateReadStream()
            {
                Throw.CheckState( !IsDirectory );
                Debug.Assert( Blob != null );
                return Blob.GetContentStream();
            }

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                Throw.CheckState( IsDirectory );
                return ((Tree)_e.Target).Select( t => new TreeEntryWrapper( t, _c ) ).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        void RefreshBranches()
        {
            if( !_branchRefreshed )
            {
                foreach( var b in Git.Branches )
                {
                    if( b.IsRemote ) _remoteBranchesFolder.Add( b );
                    else _branchesFolder.Add( b );
                }
                _branchRefreshed = true;
            }
        }
        // Currently unused. To be tested.
        internal IEnumerable<IFileInfo> FindFiles( NormalizedPath sub, Func<NormalizedPath, bool>? directoryMatch, Func<NormalizedPath, bool>? fileMatch )
        {
            if( sub.IsEmptyPath )
            {
                return Throw.NotSupportedException<IEnumerable<IFileInfo>>( "FindFiles in head, branches and remotes at once is not supported." );
            }
            if( IsInWorkingFolder( ref sub ) )
            {
                return FileSystem.PhysicalFindFiles( DisplayPath.Combine( sub ), fileMatch, directoryMatch );
            }
            RefreshBranches();
            if( sub.FirstPart == _branchesFolder.Name )
            {
                return Throw.NotSupportedException<IEnumerable<IFileInfo>>( "FindFiles is supported only when the branch is checked out." );
            }
            if( sub.FirstPart == _remoteBranchesFolder.Name )
            {
                return Throw.NotSupportedException<IEnumerable<IFileInfo>>( "FindFiles in remotes is not supported." );
            }
            return Array.Empty<IFileInfo>();

        }

        internal IFileInfo? GetFileInfo( NormalizedPath sub )
        {
            if( sub.IsEmptyPath ) return _thisDir;
            if( IsInWorkingFolder( ref sub ) )
            {
                if( sub.IsEmptyPath ) return _headFolder;
                return FileSystem.PhysicalGetFileInfo( DisplayPath.Combine( sub ) );
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
            if( sub.IsEmptyPath ) return _thisDir;
            if( IsInWorkingFolder( ref sub ) )
            {
                if( sub.IsEmptyPath ) return _headFolder;
                return FileSystem.PhysicalGetDirectoryContents( DisplayPath.Combine( sub ) );
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
            if( sub.Parts.Count > 1 && Git.Head.FriendlyName == sub.Parts[1] )
            {
                sub = sub.RemoveParts( 0, 2 );
                return true;
            }
            return false;
        }

    }
}
