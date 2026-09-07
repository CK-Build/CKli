using CK.Core;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Encapsulate the <see cref="GitRepository"/> with its <see cref="LocalWorldName"/> (the default one and the LTS ones)
/// but at most one <see cref="World"/> (if it has been opened on a World).
/// <para>
/// There are only 2 ways to obtain a StackRepository:
/// <list type="bullet">
///     <item>
///         Calling <see cref="TryOpenFromPath"/>, <see cref="OpenFromPath"/>, <see cref="TryOpenWorldFromPath"/>, <see cref="OpenWorldFromPath"/>
///         from any local path.
///     </item>
///     <item>
///         Calling <see cref="CloneAsync(IActivityMonitor, CKli.Core.CKliEnv, Uri, bool, bool, bool, string, CancellationToken)"/>
///         from the remote Uri of the stack.
///     </item>
/// </list>
/// </para>
/// </summary>
public sealed partial class StackRepository : IDisposable
{
    /// <summary>
    /// Public stack folder name.
    /// </summary>
    public const string PublicStackName = ".PublicStack";

    /// <summary>
    /// Private stack folder name.
    /// </summary>
    public const string PrivateStackName = ".PrivateStack";

    /// <summary>
    /// Prefix for duplicated stack.
    /// </summary>
    public const string DuplicatePrefix = "DuplicateOf-";

    readonly GitRepository _git;
    readonly NormalizedPath _stackRoot;
    readonly CKliEnv _context;
    readonly NormalizedPath _localProxyRepositoriesPath;
    // No DuplicatePrefix here.
    readonly string _stackName;
    LocalWorldName? _defaultWorldName;
    ImmutableArray<LocalWorldName> _worldNames;
    World? _world;
    NormalizedPath _localFolderPath;

    /// <summary>
    /// Internal access to the CKliEnv: this is only used to obtain the <see cref="CKliEnv.Committer"/> when
    /// initializing new repositories. CKliEnv must be parameter injected everywhere it is needed for better
    /// maintainability.
    /// </summary>
    internal CKliEnv Context => _context;

    /// <summary>
    /// Gets the root path of the stack (the parent folder of the ".PrivateStack" or ".PublicStack" folder).
    /// </summary>
    public NormalizedPath StackRoot => _stackRoot;

    /// <summary>
    /// Gets the GitRepository.
    /// <para>
    /// This should be used with care and mainly from unit tests: nominal use of a StackDirectory
    /// doesn't require a direct access to the repository. 
    /// </para>
    /// </summary>
    public GitRepository GitRepository => _git;

    /// <summary>
    /// Gets the path of the ".PrivateStack" or ".PublicStack". 
    /// </summary>
    public NormalizedPath StackWorkingFolder => _git.WorkingFolder;

    /// <summary>
    /// Gets the git ignored ".PrivateStack/$Local" or ".PublicStack/$Local" folder. 
    /// </summary>
    public NormalizedPath LocalFolderPath => _localFolderPath.IsEmptyPath
                                                ? (_localFolderPath = _git.WorkingFolder.AppendPart( "$Local" ))
                                                : _localFolderPath;

    /// <summary>
    /// Gets the <see cref="StackName"/>/<see cref="PublicStackName"/> (or <see cref="PrivateStackName"/>) path.
    /// </summary>
    public NormalizedPath GitDisplayPath => _git.DisplayPath;

    /// <summary>
    /// Gets the name of this stack that is necessarily the last part of the <see cref="StackRoot"/>
    /// unless <see cref="IsDuplicate"/> is true.
    /// </summary>
    public string StackName => _stackName;

    /// <summary>
    /// Gets whether this Stack is a duplicate clone: it is in a "<see cref="DuplicatePrefix"/><see cref="StackName"/>/" folder.
    /// </summary>
    public bool IsDuplicate => !ReferenceEquals( _stackName, _stackRoot.LastPart );

    /// <summary>
    /// Gets whether this stack is public.
    /// </summary>
    public bool IsPublic => _git.RepositoryKey.IsPublic;

    /// <summary>
    /// Gets the stack's repository url.
    /// </summary>
    public Uri OriginUrl => _git.RepositoryKey.OriginUrl;

    /// <summary>
    /// Gets a non empty path if this stack's <see cref="OriginUrl"/> is a file.
    /// <para>
    /// When not empty, this is the folder that contains the stack's remote repository.
    /// It can contain other local repositories: these "remotes" can be true "remote proxies" (if they
    /// have an 'origin') or purely local remotes (this is used for tests).
    /// </para>
    /// <para>
    /// When <see cref="WorldDefinitionFile"/> detects &lt;Repository Url="file:///..." /&gt; that are
    /// actually located in this local proxy repositories folder, the Url is normalized to the
    /// repository name ("file:///C:/Dev/CKli/Tests/CKli.Core.Tests/Remotes/CKt/CKt-Core" is replaced by "CKt-Core").
    /// </para>
    /// </summary>
    public NormalizedPath LocalProxyRepositoriesPath => _localProxyRepositoriesPath;

    /// <summary>
    /// Gets the secrets store.
    /// </summary>
    public ISecretsStore SecretsStore => _context.SecretsStore;

    /// <summary>
    /// Gets the default world name (no <see cref="WorldName.LTSName"/>).
    /// </summary>
    public LocalWorldName DefaultWorldName
    {
        get
        {
            _defaultWorldName ??= new LocalWorldName( this, null, _stackRoot, StackWorkingFolder.AppendPart( $"{StackName}.xml" ) );
            return _defaultWorldName;
        }
    }

    /// <summary>
    /// Gets all the worlds that this stack contains starting with the <see cref="DefaultWorldName"/>
    /// and lexicographically sorted.
    /// <para>
    /// Worlds are defined by the xml World definition files "StackName[@LTSName].xml" in this stack repository <see cref="StackWorkingFolder"/>.
    /// </para>
    /// </summary>
    public ImmutableArray<LocalWorldName> WorldNames
    {
        get
        {
            if( _worldNames.IsDefault )
            {
                _worldNames = Directory.GetFiles( StackWorkingFolder, $"{StackName}@*.xml" )
                                .Select( p => TryParseDefinitionFilePath( this, p ) )
                                .Where( w => w != null )
                                .OrderBy( n => n!.FullName )
                                .Prepend( DefaultWorldName )
                                .ToImmutableArray()!;
            }
            return _worldNames;

            static LocalWorldName? TryParseDefinitionFilePath( StackRepository stack, NormalizedPath path )
            {
                Throw.DebugAssert( !path.IsEmptyPath
                                   && path.Parts.Count >= 4
                                   && path.LastPart.EndsWith( ".xml", StringComparison.OrdinalIgnoreCase ) );
                var fName = path.LastPart;
                Throw.DebugAssert( ".xml".Length == 4 );
                fName = fName.Substring( 0, fName.Length - 4 );
                if( !WorldName.TryParse( fName, out var stackName, out var ltsName ) ) return null;
                Throw.DebugAssert( stackName == stack.StackName );
                var wRoot = stack.StackRoot;
                if( ltsName != null )
                {
                    return new LocalWorldName( stack, ltsName, wRoot.AppendPart( ltsName ), path );
                }
                return new LocalWorldName( stack, null, wRoot, path );
            }
        }
    }

    /// <summary>
    /// Gets the world from a <paramref name="path"/> that must start with <see cref="StackRoot"/>.
    /// <para>
    /// This tries to find a LTS world if the path is below StackRoot and its first folder starts with '@' and is
    /// a valid LTS name. When the path is everywhere else, <see cref="DefaultWorldName"/> is used.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="path">The path that must be or start with <see cref="StackRoot"/> or an <see cref="ArgumentException"/> is thrown.</param>
    /// <returns>The world name for the path or null on error.</returns>
    public LocalWorldName? GetWorldNameFromPath( IActivityMonitor monitor, NormalizedPath path )
    {
        Throw.CheckArgument( path.Path.StartsWith( StackRoot, StringComparison.OrdinalIgnoreCase ) );

        if( path.Parts.Count > _stackRoot.Parts.Count
            && WorldName.IsValidLTSName( path.Parts[_stackRoot.Parts.Count] ) )
        {
            var ltsName = path.Parts[_stackRoot.Parts.Count];
            var worldName = WorldNames.FirstOrDefault( n => n.LTSName == ltsName );
            if( worldName == null )
            {
                monitor.Error( $"Stack '{StackName}' doesn't contain a LTS world '{ltsName}'." );
            }
            return worldName;
        }
        return DefaultWorldName.CheckDefinitionFileExists( monitor ) ? _defaultWorldName : null;
    }

    /// <summary>
    /// Creates a commit if needed.
    /// A stack never tries to amend commits: commits are always <see cref="CommitBehavior.CreateNewCommit"/>
    /// and no empty commits are done.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="commitMessage">The commit message.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Commit( IActivityMonitor monitor, string commitMessage )
    {
        return _git.Commit( monitor, commitMessage, CommitBehavior.CreateNewCommit ) != CommitResult.Error;
    }

    /// <summary>
    /// Resets the working folder to its committed state. Also deletes any untracked files.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool ResetHard( IActivityMonitor monitor )
    {
        return _git.ResetHard( monitor, out _ );
    }

    /// <summary>
    /// Gets the branch to work on: <paramref name="stackBranchName"/> when the repository has it (locally or on
    /// its "origin" remote), the repository's current branch otherwise.
    /// <para>
    /// A Stack repository has a single branch, "main" by convention: this is the branch that <see cref="CreateAsync"/>
    /// creates. A Stack repository that predates this convention has a "master" one (or any other name) and creating
    /// a purely local "main" for it - what <see cref="GitRepository.FullCheckout(IActivityMonitor, string, bool)"/>
    /// and <see cref="GitRepository.EnsureBranch(IActivityMonitor, string, LogLevel, LibGit2Sharp.Commit?)"/> do when
    /// the branch is nowhere to be found - gives a Stack that can never be pushed back: <see cref="PushChanges"/>
    /// pushes the head and the head must track a remote branch.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="git">The stack repository.</param>
    /// <param name="stackBranchName">The wanted branch name.</param>
    /// <returns>The branch name to use.</returns>
    static string GetStackBranchName( IActivityMonitor monitor, GitRepository git, string stackBranchName )
    {
        // GetBranch creates the local branch that tracks the "origin/{stackBranchName}" one when it exists:
        // this is what the callers below need anyway.
        if( git.GetBranch( monitor, stackBranchName, LogLevel.None ) != null )
        {
            return stackBranchName;
        }
        var actual = git.CurrentBranchName;
        monitor.Warn( $"""
            Stack repository '{git.DisplayPath}' has no '{stackBranchName}' branch: working on its current branch '{actual}'.
            """ );
        return actual;
    }

    /// <summary>
    /// Commits and push changes to the remote.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="force">
    /// True to force the push. This is required when the head is not a descendant of the remote branch: this is
    /// the case right after a <see cref="SetRemoteUrl(IActivityMonitor, Uri, bool)"/> to a brand new repository
    /// since some hosting providers create it with an initial commit of their own (an unrelated history).
    /// <para>
    /// Caution: this discards whatever the remote branch holds.
    /// </para>
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public bool PushChanges( IActivityMonitor monitor, bool force = false )
    {
        CommitResult result = _git.Commit( monitor, "Automatic pre-push commit." );
        if( result == CommitResult.Error ) return false;
        var head = _git.Repository.Head;
        if( !force )
        {
            return _git.PushBranch( monitor, head, autoCreateRemoteBranch: false );
        }
        return _git.GetRemote( monitor, "origin", forWrite: true, out var remote, out var creds )
               && _git.Push( monitor, remote, creds, [$"+{head.CanonicalName}:{head.CanonicalName}"] );
    }

    /// <summary>
    /// The local git configuration key that holds the <see cref="MigrationSourceUrl"/>.
    /// </summary>
    public const string MigrationSourceConfigKey = "ckli.migratedFrom";

    /// <summary>
    /// Gets the url of the remote repository that this Stack has been migrated from and that has not reached its
    /// final state (archived) yet. Null when no migration is pending.
    /// <para>
    /// <see cref="SetRemoteUrl(IActivityMonitor, Uri, bool)"/> sets it before changing the url so that the
    /// "ckli remote stack migrate" command can finish its job even when it is interrupted, and that command
    /// clears it once the previous repository is archived (or cannot be).
    /// </para>
    /// </summary>
    public Uri? MigrationSourceUrl
    {
        get
        {
            var v = _git.Repository.Config.Get<string>( MigrationSourceConfigKey )?.Value;
            return v != null && Uri.TryCreate( v, UriKind.Absolute, out var url ) ? url : null;
        }
    }

    /// <summary>
    /// Sets or clears (null <paramref name="url"/>) the <see cref="MigrationSourceUrl"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="url">The url to remember or null to forget it.</param>
    /// <returns>True on success, false on error.</returns>
    public bool SetMigrationSourceUrl( IActivityMonitor monitor, Uri? url )
    {
        try
        {
            if( url == null )
            {
                if( _git.Repository.Config.Get<string>( MigrationSourceConfigKey ) != null )
                {
                    _git.Repository.Config.Unset( MigrationSourceConfigKey );
                }
            }
            else
            {
                _git.Repository.Config.Set( MigrationSourceConfigKey, url.ToString() );
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While updating the '{MigrationSourceConfigKey}' configuration of '{_git.DisplayPath}'.", ex );
            return false;
        }
    }

    /// <summary>
    /// Changes the stack's "origin" remote.
    /// <see cref="GitRepositoryKey.ThrowArgumentExceptionOnInvalidUrl"/> is called.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="newUrl">The new remote URL. Must be a valid, normalized URL.</param>
    /// <param name="push">
    /// True to <see cref="PushChanges(IActivityMonitor, bool)"/> once the url has been changed.
    /// <para>
    /// Caution: the credentials are the ones of the <see cref="GitRepository.RepositoryKey"/>, that has been
    /// resolved from the url this repository has been OPENED with. A migration that changes the host or the
    /// repository owner must use false here and push from a Stack reopened on the new url (this is what the
    /// "ckli remote stack migrate" command does).
    /// </para>
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public bool SetRemoteUrl( IActivityMonitor monitor, Uri newUrl, bool push = true )
    {
        GitRepositoryKey.ThrowArgumentExceptionOnInvalidUrl( newUrl, nameof( newUrl ) );

        var oldUrl = _git.RepositoryKey.OriginUrl;
        if( GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer.Equals( oldUrl, newUrl ) )
        {
            monitor.Info( $"Remote URL is already '{newUrl}'." );
            return !push || PushChanges( monitor );
        }

        using( monitor.OpenInfo( $"Changing 'origin' remote url from '{oldUrl}' to '{newUrl}'." ) )
        {
            // Remembers where we come from BEFORE changing anything: this is what enables the migration to
            // archive the previous repository even if it is interrupted below (see MigrationSourceUrl).
            if( !SetMigrationSourceUrl( monitor, oldUrl ) )
            {
                return false;
            }
            // Update the git remote
            try
            {
                _git.Repository.Network.Remotes.Update( "origin", r => r.Url = newUrl.AbsoluteUri );
                monitor.Trace( "Git remote 'origin' updated." );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Failed to update git remote 'origin'.", ex );
                SetMigrationSourceUrl( monitor, null );
                return false;
            }

            // Update the registry
            try
            {
                Registry.RegisterNewStack( monitor, StackWorkingFolder, newUrl );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Failed to update registry.", ex );
                // Try to revert the git remote change
                try
                {
                    _git.Repository.Network.Remotes.Update( "origin", r => r.Url = oldUrl.AbsoluteUri );
                    monitor.Warn( "Reverted git remote to original url due to registry update failure." );
                    // Reverted: there is no migration in progress anymore.
                    SetMigrationSourceUrl( monitor, null );
                }
                catch
                {
                    // The remote is still the new url: the MigrationSourceUrl is kept so that a migration
                    // can still archive the previous repository.
                    monitor.Error( "Failed to revert git remote after registry update failure. Manual fix required." );
                }
                return false;
            }
        }
        return !push || PushChanges( monitor );
    }

    StackRepository( GitRepository git, in NormalizedPath stackRoot, CKliEnv context, string stackName )
    {
        _git = git;
        _stackRoot = stackRoot;
        _context = context;
        _stackName = stackName;
        var originUrl = git.RepositoryKey.OriginUrl;
        if( originUrl.IsFile )
        {
            var p = new NormalizedPath( originUrl.LocalPath );
            if( p.Parts.Count > 1 )
            {
                _localProxyRepositoriesPath = p.RemoveLastPart();
            }
        }
    }

    /// <summary>
    /// Tries to open a stack directory from a path.
    /// This lookups the ".PrivateStack" or ".PublicStack" in and above <see cref="CKliEnv.CurrentDirectory"/>: if none
    /// are found, there is no <paramref name="error"/> and null is returned.
    /// <para>
    /// On success, the stack repository is on the <paramref name="stackBranchName"/> and, by default,
    /// the stack and its world definitions has been updated (but not the repositories of any of the worlds):
    /// use <paramref name="skipPullStack"/> to leave the local stack untouched.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The basic command context.</param>
    /// <param name="error">True on error, false on success.</param>
    /// <param name="skipPullStack">True to leave the stack repository as-is. By default, a pull is done from the remote stack repository.</param>
    /// <param name="stackBranchName">Specifies a branch name. There should be no reason to use multiple branches in a stack repository.</param>
    /// <returns>The resulting stack repository if found and opened successfully. May be null if not found.</returns>
    public static StackRepository? TryOpenFromPath( IActivityMonitor monitor,
                                                    CKliEnv context,
                                                    out bool error,
                                                    bool skipPullStack = false,
                                                    string stackBranchName = "main" )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( stackBranchName );
        error = false;
        var gitPath = context.CurrentStackPath;
        if( gitPath.IsEmptyPath ) return null;

        var isPublic = gitPath.LastPart == PublicStackName;
        var git = GitRepository.Open( monitor,
                                      context.SecretsStore,
                                      context.Committer,
                                      gitPath,
                                      gitPath.RemoveFirstPart( gitPath.Parts.Count - 2 ),
                                      isPublic );
        if( git != null )
        {
            var stackRoot = gitPath.RemoveLastPart();
            if( git.RepositoryKey.CheckOriginUrlStackSuffix( monitor, out var stackNameFromUrl ) )
            {
                if( stackRoot.LastPart.Equals( stackNameFromUrl, StringComparison.OrdinalIgnoreCase ) )
                {
                    // Use the same reference for non duplicate: ReferenceEquals is used
                    // and the actual name of the Stack is the folder name with the right case
                    // that has been fixed by the Clone.
                    stackNameFromUrl = stackRoot.LastPart;
                }
                else if( !stackRoot.LastPart.Equals( DuplicatePrefix + stackNameFromUrl, StringComparison.OrdinalIgnoreCase ) )
                {
                    monitor.Error( $"Stack folder '{stackRoot.LastPart}' must be '{stackNameFromUrl}' or '{DuplicatePrefix}{stackNameFromUrl}' (case insensitive) since repository Url is '{git.RepositoryKey.OriginUrl}'." );
                    error = true;
                }
                if( !error )
                {
                    var b = git.EnsureBranch( monitor, GetStackBranchName( monitor, git, stackBranchName ) );
                    if( git.Checkout( monitor, b )
                        && (skipPullStack || git.FetchMergeHead( monitor, LibGit2Sharp.MergeFileFavor.Theirs )) )
                    {
                        return new StackRepository( git, stackRoot, context, stackNameFromUrl );
                    }
                    error = true;
                }
            }
            git.Dispose();
        }
        else
        {
            error = true;
        }
        return null;
    }

    /// <summary>
    /// Open a stack from a path.
    /// This lookups the ".PrivateStack" or ".PublicStack" in and above <see cref="CKliEnv.CurrentDirectory"/>: a stack
    /// must be found otherwise it is an error.
    /// <para>
    /// On success, the stack repository is on the <paramref name="stackBranchName"/> and, by default,
    /// the stack and its world definitions has been updated (but not the repositories of any of the worlds):
    /// use <paramref name="skipPullStack"/> to leave the local stack untouched.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The basic command context.</param>
    /// <param name="stack">The non null stack on success.</param>
    /// <param name="skipPullStack">True to leave the stack repository as-is. By default, a pull is done from the remote stack repository.</param>
    /// <param name="stackBranchName">Specifies a branch name. There should be no reason to use multiple branches in a stack repository.</param>
    /// <returns>The resulting stack repository if found and opened successfully. May be null if not found.</returns>
    public static bool OpenFromPath( IActivityMonitor monitor,
                                     CKliEnv context,
                                     [NotNullWhen( true )] out StackRepository? stack,
                                     bool skipPullStack = false,
                                     string stackBranchName = "main" )
    {
        stack = TryOpenFromPath( monitor, context, out bool error, skipPullStack, stackBranchName );
        if( error )
        {
            Throw.DebugAssert( stack == null );
            return false;
        }
        if( stack == null )
        {
            monitor.Error( $"Unable to find a stack repository from path '{context.CurrentDirectory}'." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Tries to open a stack and a world from a <see cref="CKliEnv.CurrentDirectory"/>. If no stack is found on or above the path,
    /// this is not an error but <c>(null,null)</c> is returned.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The basic command context.</param>
    /// <param name="error">True on error, false on success.</param>
    /// <param name="skipPullStack">True to leave the stack repository as-is. By default, a pull is done from the remote stack repository.</param>
    /// <param name="withPlugins">False to totally skip plugin loading. This should only be used in very special scenarii.</param>
    /// <returns>The resulting stack repository and world on success. Both are null on error of if no stack is found.</returns>
    public static (StackRepository? Stack, World? World) TryOpenWorldFromPath( IActivityMonitor monitor,
                                                                               CKliEnv context,
                                                                               out bool error,
                                                                               bool skipPullStack = false,
                                                                               bool withPlugins = true )
    {
        var stack = TryOpenFromPath( monitor, context, out error, skipPullStack );
        if( stack != null )
        {
            var w = World.Create( monitor,
                                  context.Screen.ScreenType,
                                  stack,
                                  context.CurrentDirectory,
                                  withPlugins );
            if( w == null )
            {
                error = true;
                stack.Dispose();
            }
            else
            {
                stack._world = w;
                return (stack, w);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Opens a stack and a world from a <see cref="CKliEnv.CurrentDirectory"/>.
    /// A stack must be found on or above the path otherwise it is an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The basic command context.</param>
    /// <param name="stack">The non null stack on success.</param>
    /// <param name="world">The non null world on success.</param>
    /// <param name="skipPullStack">True to leave the stack repository as-is. By default, a pull is done from the remote stack repository.</param>
    /// <param name="withPlugins">False to totally skip plugin loading. This should only be used in very special scenarii.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool OpenWorldFromPath( IActivityMonitor monitor,
                                          CKliEnv context,
                                          [NotNullWhen( true )] out StackRepository? stack,
                                          [NotNullWhen( true )] out World? world,
                                          bool skipPullStack = false,
                                          bool withPlugins = true )
    {
        world = null;
        stack = TryOpenFromPath( monitor, context, out bool error, skipPullStack );
        if( error )
        {
            return false;
        }
        if( stack == null )
        {
            monitor.Error( $"No stack found for path '{context.CurrentDirectory}'." );
            return false;
        }
        world = World.Create( monitor,
                              context.Screen.ScreenType,
                              stack,
                              context.CurrentDirectory,
                              withPlugins );
        if( world == null )
        {
            stack.Dispose();
            stack = null;
            return false;
        }
        stack._world = world;
        return true;
    }

    /// <summary>
    /// Clones a Stack and all its default world repositories to the local file system in a new folder
    /// from a stack repository.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">
    /// The context. <see cref="CKliEnv.CurrentStackPath"/> must be empty (since the <see cref="StackRoot"/> will be created
    /// in <see cref="CKliEnv.CurrentDirectory"/>). This context is immutable. To open the newly cloned stack, a new CKLiEnv must be
    /// obtained (for instance by calling <see cref="CKliEnv.ChangeDirectory(NormalizedPath)"/>).
    /// </param>
    /// <param name="url">The url of the remote. <see cref="GitRepositoryKey.ThrowArgumentExceptionOnInvalidUrl(Uri?, string)"/> is called.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    /// <param name="allowDuplicateStack">
    /// True to create a "DuplicateOf-XX" stack folder if the stack is already available on this machine.
    /// </param>
    /// <param name="ignoreParentStack">
    /// True to allow a stack to be cloned in an existing one.
    /// This should be avoided (but is required in the tests of Stack plugins).
    /// </param>
    /// <param name="stackBranchName">
    /// Specifies a branch name.
    /// There should be no reason to use multiple branches in a stack repository.
    /// </param>
    /// <param name="maxDop">
    /// Maximal parallel repository clone.
    /// </param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The repository or null on error.</returns>
    public static async Task<StackRepository?> CloneAsync( IActivityMonitor monitor,
                                                           CKliEnv context,
                                                           Uri url,
                                                           bool isPublic,
                                                           bool allowDuplicateStack = false,
                                                           bool ignoreParentStack = false,
                                                           string stackBranchName = "main",
                                                           int maxDop = 0,
                                                           CancellationToken cancellation = default )
    {
        bool isTestRun = CKliRootEnv.IsTestRun;
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( context );
        // The nominal case is that we cannot clone a stack inside another stack. But when ignoreParentStack
        // is specified or we are running under a test harness, we allow this.
        if( !ignoreParentStack && !isTestRun && !context.CurrentStackPath.IsEmptyPath)
        {
            monitor.Error( $"""
                A stack exists above at '{context.CurrentStackPath}'.
                The option flag --ignore-parent-stack must be specified if this is intended.
                """ );
            return null;
        }
        Throw.CheckNotNullArgument( stackBranchName );
        var parentPath = context.CurrentDirectory;
        if( !parentPath.IsRooted
            || parentPath.Parts.Count < 2
            || parentPath.LastPart.Equals( PublicStackName, StringComparison.OrdinalIgnoreCase )
            || parentPath.LastPart.Equals( PrivateStackName, StringComparison.OrdinalIgnoreCase ) )
        {
            monitor.Error( $"Invalid path '{parentPath}': it must be rooted and not end with {PublicStackName} or {PrivateStackName}." );
            return null;
        }

        var stackGitKey = GitRepositoryKey.Create( monitor, context.SecretsStore, url, isPublic );
        if( stackGitKey == null
            || !stackGitKey.CheckOriginUrlStackSuffix( monitor, out var stackNameFromUrl ) )
        {
            return null;
        }
        // Default folder name is the stackNameFromUrl.
        var stackFolderName = stackNameFromUrl;
        var already = Registry.CheckExistingStack( monitor, url );
        if( already.Count > 0 )
        {
            var common = $"""
                         The stack '{stackNameFromUrl}' at '{url}' is already available here:
                         {already.Select( p => p.Path ).Concatenate( Environment.NewLine )}
                         """;
            if( !allowDuplicateStack )
            {
                monitor.Error( common + """

                    The option flag --allow-duplicate must be specified if this is intended.
                    """ );
                return null;
            }
            monitor.Warn( common );
            stackFolderName = DuplicatePrefix + stackNameFromUrl;
        }
        var stackRoot = parentPath.AppendPart( stackFolderName );

        // Secure Stack inside Stack scenario.
        if( !ignoreParentStack )
        {
            // Under a test harness, the cloned folder regularly lives inside the stack being tested:
            // never relocate then. (This used to be restricted to a hard-coded "/CKli/.PublicStack" parent,
            // which only ever matched CKli's own stack.)
            var parentStack = FindGitStackPath( parentPath );
            if( !parentStack.IsEmptyPath && !isTestRun )
            {
                var stackAbove = parentStack.RemoveLastPart();
                var safeRoot = stackAbove.RemoveLastPart().AppendPart( stackFolderName );
                monitor.Warn( $"Resolved stack path '{stackRoot}' is inside stack '{stackAbove}': moving it to {safeRoot}." );
                stackRoot = safeRoot;
            }
        }

        // Don't clone if the resolved path exists.
        if( Path.Exists( stackRoot ) )
        {
            monitor.Error( $"The resolved path to clone '{stackRoot}' already exists." );
            return null;
        }

        // The NormalizedPath keeps (MUST keep!) the LastPart reference.
        // This invariant is... strange but it forces the code above and below to be rigorous.
        Throw.DebugAssert( "IsDuplicate uses ReferenceEquals.",
            (ReferenceEquals( stackRoot.LastPart, stackFolderName ) && stackFolderName != stackNameFromUrl)
            || (ReferenceEquals( stackRoot.LastPart, stackNameFromUrl ) && stackFolderName == stackNameFromUrl) );

        NormalizedPath gitPath = stackRoot.AppendPart( isPublic ? PublicStackName : PrivateStackName );

        var git = GitRepository.Clone( monitor,
                                       stackGitKey,
                                       context.Committer,
                                       gitPath,
                                       gitPath.RemoveFirstPart( gitPath.Parts.Count - 2 ),
                                       cancellation );
        if( git != null )
        {
            // The clone checked out the remote's default branch: this is the one to work on when the
            // remote has no "main" branch.
            stackBranchName = GetStackBranchName( monitor, git, stackBranchName );
            // Before doing anything else, we read the definition file and extract the actual
            // world name with the right casing. If case differ, the git handle is disposed,
            // the folder name is fixed and a new git handle is acquired on the new path.
            if( git.FullCheckout( monitor, stackBranchName, skipFetchMerge: true )
                && GetActualStackName( monitor, git, stackNameFromUrl, out var actualStackName ) )
            {
                if( actualStackName != stackNameFromUrl )
                {
                    using( monitor.OpenWarn( $"""
                        Stack name is actually '{actualStackName}' (not '{stackNameFromUrl}').
                        Renaming the Stack folder name to be '{actualStackName}'.
                        """ ) )
                    {
                        git.Dispose();
                        var isDuplicate = stackFolderName != stackNameFromUrl;
                        var newStackFolderName = isDuplicate ? DuplicatePrefix + actualStackName : actualStackName;
                        var newStackRoot = stackRoot.RemoveLastPart().AppendPart( newStackFolderName );
                        if( !FileHelper.MoveFolder( monitor, stackRoot, newStackRoot ) )
                        {
                            return null;
                        }
                        stackFolderName = newStackFolderName;
                        stackRoot = newStackRoot;
                        gitPath = stackRoot.AppendPart( isPublic ? PublicStackName : PrivateStackName );
                        stackNameFromUrl = actualStackName;

                        Throw.DebugAssert( "We kept the 'IsDuplicate uses ReferenceEquals' invariant.",
                            (ReferenceEquals( stackRoot.LastPart, stackFolderName ) && stackFolderName != stackNameFromUrl)
                            || (ReferenceEquals( stackRoot.LastPart, stackNameFromUrl ) && stackFolderName == stackNameFromUrl) );

                        // The handle above has been disposed to be able to move the folder: everything below
                        // (and the returned StackRepository) needs a valid one, bound to the new path and to
                        // the new display path.
                        var moved = GitRepository.Open( monitor,
                                                        context.SecretsStore,
                                                        context.Committer,
                                                        gitPath,
                                                        gitPath.RemoveFirstPart( gitPath.Parts.Count - 2 ),
                                                        isPublic );
                        if( moved == null ) return null;
                        git = moved;
                    }
                }
                SetupNewLocalDirectory( gitPath );
                Registry.RegisterNewStack( monitor, gitPath, url );
                var result = new StackRepository( git, stackRoot, context, stackNameFromUrl );
                // Now we can clone the world's repositories.
                if( await CloneWorldAsync( monitor, result, result.DefaultWorldName, maxDop, cancellation ) )
                {
                    return result;
                }
                result.Dispose();
            }
            git.Dispose();
            return null;
        }
        return null;

        static async Task<bool> CloneWorldAsync( IActivityMonitor monitor,
                                                 StackRepository stack,
                                                 LocalWorldName world,
                                                 int maxDop,
                                                 CancellationToken cancellation )
        {
            var definitionFile = world.LoadDefinitionFile( monitor );
            if( definitionFile == null ) return false;
            var layout = definitionFile.ReadLayout( monitor );
            if( layout == null ) return false;

            using( monitor.OpenInfo( $"Cloning {layout.Count} repositories in {stack.StackRoot} ({(maxDop <= 0 ? "parallel" : $"--max-dop {maxDop}")})." ) )
            {
                var pool = new ActivityMonitorAsyncPool( maxDop <= 0 ? int.MaxValue : maxDop );
                return await pool.ParallelAsync( layout,
                                                 ( monitor, l, cancellation ) => GitRepository.TryCloneWorkingFolder( monitor,
                                                                                                                      new GitRepositoryKey( stack.SecretsStore, l.Url, stack.IsPublic ),
                                                                                                                      world.WorldRoot.Combine( l.Path ),
                                                                                                                      cancellation ),
                                                 ParallelErrorBehavior.SoftStop,
                                                 cancellation )
                                 .ConfigureAwait( false );
            }
        }
    }

    /// <summary>
    /// Creates a new empty Stack in the current directory with its new remote repository.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The context. <see cref="CKliEnv.CurrentStackPath"/> must be empty.</param>
    /// <param name="url">Remote url. Must end with "-Stack" and be handled by one of the <see cref="GitHostingProvider"/>.</param>
    /// <param name="isPublic">Whether the new Stack must be public.</param>
    /// <param name="ignoreParentStack">
    /// True to allow a stack to be created in an existing one.
    /// This should be avoided (but is required in the tests of Stack plugins).
    /// </param>
    /// <param name="cancellation">Cancellation token.</param>
    /// <returns>The repository or null on error.</returns>
    public static async Task<StackRepository?> CreateAsync( IActivityMonitor monitor,
                                                            CKliEnv context,
                                                            Uri url,
                                                            bool isPublic,
                                                            bool ignoreParentStack,
                                                            CancellationToken cancellation )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( context );

        // First, handles the url.
        var gitKey = GitRepositoryKey.Create( monitor, context.SecretsStore, url, isPublic );
        if( gitKey == null
            || !gitKey.CheckOriginUrlStackSuffix( monitor, out var stackName ) )
        {
            return null;
        }
        var already = Registry.CheckExistingStack( monitor, url );
        if( already.Count > 0 )
        {
            monitor.Error(  $"""
                        The stack '{stackName}' at '{url}' is already available here:
                        {already.Select( p => p.Path ).Concatenate( Environment.NewLine )}
                        """ );
            return null;
        }
        if( !gitKey.TryGetHostingInfo( monitor, out var hostingProvider, out var remoteRepoPath ) )
        {
            return null;
        }

        // Second, handles the local path.

        var p = context.CurrentDirectory;
        if( !p.IsRooted
            || p.Parts.Count < 2
            || p.LastPart.Equals( PublicStackName, StringComparison.OrdinalIgnoreCase )
            || p.LastPart.Equals( PrivateStackName, StringComparison.OrdinalIgnoreCase ) )
        {
            monitor.Error( $"Invalid path '{p}': it must be rooted and not end with {PublicStackName} or {PrivateStackName}." );
            return null;
        }
        var stackRoot = p.AppendPart( stackName );
        if( Path.Exists( stackRoot ) )
        {
            monitor.Error( $"The path '{stackRoot}' already exists." );
            return null;
        }
        // Check we're not inside an existing stack. Don't trust the context.CurrentStackPath here (tests skips it).
        if( !ignoreParentStack )
        {
            var parentStack = FindGitStackPath( stackRoot );
            if( !parentStack.IsEmptyPath )
            {
                monitor.Error( $"Cannot create stack inside existing stack '{parentStack.RemoveLastPart()}'." );
                return null;
            }
        }
        // Everything seems okay. It's time to create the remote before cloning it.
        var remoteInfo = await hostingProvider.CreateRepositoryAsync( monitor, remoteRepoPath, !isPublic, "main", cancellation ).ConfigureAwait( false );
        if( remoteInfo == null )
        {
            return null;
        }
        // Remote (empty) repository has been created: we can clone, initialize (default world definition file and $Local folder), commit and push it.
        // Centralized compensation by encapsulating the initialization.

        GitRepository? gitRepository = null;
        StackRepository? newStack = null;
        try
        {
            newStack = Initialize( monitor,
                                   context,
                                   stackRoot,
                                   gitKey,
                                   isPublic,
                                   stackName,
                                   out gitRepository,
                                   cancellation );
        }
        catch( Exception ex )
        {
            monitor.Error( "While initializing new stack.", ex );
        }
        if( newStack == null )
        {
            try
            {
                gitRepository?.Dispose();
            }
            catch( Exception ex )
            {
                monitor.Warn( "While disposing git repository.", ex );
            }
            FileHelper.DeleteFolder( monitor, stackRoot );
            try
            {
                // If we have been canceled, we want the compensation to run: no CancelationToken.
                await hostingProvider.DeleteRepositoryAsync( monitor, remoteRepoPath, cancellation: default ).ConfigureAwait( false );
            }
            catch( Exception ex )
            {
                monitor.Warn( "While deleting newly crated repository.", ex );
            }
        }
        return newStack;

        static StackRepository? Initialize( IActivityMonitor monitor,
                                            CKliEnv context,
                                            NormalizedPath stackRoot,
                                            GitRepositoryKey gitKey,
                                            bool isPublic,
                                            string stackName,
                                            out GitRepository? gitRepository,
                                            CancellationToken cancellation )
        {
            NormalizedPath gitPath = stackRoot.AppendPart( isPublic ? PublicStackName : PrivateStackName );
            gitRepository = GitRepository.Clone( monitor,
                                                 gitKey,
                                                 context.Committer,
                                                 gitPath,
                                                 gitPath.RemoveFirstPart( gitPath.Parts.Count - 2 ),
                                                 cancellation );
            if( gitRepository == null
                || !gitRepository.FullCheckout( monitor, "main", skipFetchMerge: true ) )
            {
                return null;
            }

            // Create the initial stack definition XML file
            File.WriteAllText( gitPath.AppendPart( $"{stackName}.xml" ), $"""
                <{stackName}>
                </{stackName}>
                """ );

            // Setup $Local directory and .gitignore
            SetupNewLocalDirectory( gitPath );

            // Create initial commit (Commit method already stages all files)
            if( gitRepository.Commit( monitor, "Initial stack creation.", CommitBehavior.CreateNewCommit ) == CommitResult.Error )
            {
                monitor.Error( "Failed to create initial commit." );
                return null;
            }
            if( !gitRepository.PushBranch( monitor, gitRepository.Repository.Head, autoCreateRemoteBranch: true ) )
            {
                return null;
            }

            // Register in the stack registry.
            Registry.RegisterNewStack( monitor, gitPath, gitKey.OriginUrl );

            return new StackRepository( gitRepository, stackRoot, context, stackName );
        }
    }
    static void SetupNewLocalDirectory( NormalizedPath gitPath )
    {
        var localDir = gitPath.AppendPart( "$Local" );
        if( !Directory.Exists( localDir ) )
        {
            Directory.CreateDirectory( localDir );
            // The .gitignore ignores it. It is created only once.
            var ignore = gitPath.AppendPart( ".gitignore" );
            if( !File.Exists( ignore ) ) File.WriteAllText( ignore, """
                $Local/
                Logs/
                .vs/
                .idea/
                /CKli-Plugins/CKli.Plugins/CKli.CompiledPlugins.cs
                !.gitignore

                """ );
        }
    }

    static bool GetActualStackName( IActivityMonitor monitor, GitRepository gitStack, string stackNameFromUrl, [NotNullWhen( true )] out string? actualStackName )
    {
        actualStackName = null;
        var definitionFilePath = gitStack.WorkingFolder.AppendPart( $"{stackNameFromUrl}.xml" );
        if( !File.Exists( definitionFilePath ) )
        {
            monitor.Error( $"The expected default World definition file '{stackNameFromUrl}.xml' is missing at the root of the Stack repository." );
            return false;
        }
        try
        {
            using var r = XmlReader.Create( definitionFilePath );
            while( !r.IsStartElement() && r.Read() ) ;
            if( !r.IsStartElement() || r.Name.Length < 2 || r.Name.Contains( ':' ) )
            {
                monitor.Error( $"Unable to find a named root element in default World definition file '{stackNameFromUrl}.xml'." );
                return false;
            }
            actualStackName = r.Name;
            if( stackNameFromUrl != actualStackName
                && !stackNameFromUrl.Equals( actualStackName, StringComparison.OrdinalIgnoreCase ) )
            {
                monitor.Error( $"""
                    The url '{gitStack.RepositoryKey.OriginUrl}' contains a Stack named '{actualStackName}'.
                    Names can differ in casing but no more than that: this Stack repository is not valid.
                    """ );
                return false;

            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Error while reading default World definition file '{stackNameFromUrl}.xml'.", ex );
            return false;
        }
    }

    /// <summary>
    /// Finds a git stack here or above by looking for ".PrivateStack" or ".PublicStack" folder. 
    /// </summary>
    /// <param name="path">Starting path.</param>
    /// <returns>A git stack or the empty path.</returns>
    public static NormalizedPath FindGitStackPath( NormalizedPath path )
    {
        foreach( var tryPath in path.PathsToFirstPart( null, [PublicStackName, PrivateStackName] ) )
        {
            if( Directory.Exists( tryPath ) ) return tryPath;
        }
        return default;
    }

    // Not released, not tested yet.
    internal LocalWorldName? CreateNewLTS( IActivityMonitor monitor, string ltsName, XDocument content )
    {
        Throw.CheckArgument( content?.Root != null );
        Throw.CheckArgument( WorldName.IsValidLTSName( ltsName ) );

        var newRoot = _stackRoot.AppendPart( ltsName );
        var newDesc = _git.WorkingFolder.AppendPart( $"{StackName}{ltsName}.xml" );
        var newOne = new LocalWorldName( this, ltsName, newRoot, newDesc );

        if( File.Exists( newOne.XmlDescriptionFilePath ) )
        {
            monitor.Error( $"Unable to create '{newOne}' world: file '{newOne.XmlDescriptionFilePath}' already exists." );
            return null;
        }
        if( Directory.Exists( newOne.WorldRoot ) )
        {
            monitor.Error( $"Unable to create '{newOne}' world: directory {newOne.WorldRoot} already exists." );
            return null;
        }
        content.SafeSave( newOne.XmlDescriptionFilePath );
        Directory.CreateDirectory( newOne.WorldRoot );
        return newOne;
    }

    /// <summary>
    /// Closes this stack. This releases the world (if a world has been opened) and if the <see cref="WorldDefinitionFile"/>
    /// has been modified but not yet saved, it is saved and a commit is created.
    /// <para>
    /// Once called, it is useless to call <see cref="Dispose()"/> (but it doesn't harm).
    /// </para>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <returns>True on success, false if an error occurred when saving the definition file.</returns>
    public bool Close( IActivityMonitor monitor )
    {
        bool success = true;
        if( _world != null )
        {
            if( _world.DefinitionFile.IsDirty )
            {
                success = _world.DefinitionFile.SaveFile( monitor ) && Commit( monitor, "Updated Definition file." );
            }
            _world.DisposeRepositoriesAndReleasePlugins();
            _world = null;
        }
        _git.Dispose();
        return success;
    }

    /// <summary>
    /// Close this world. This doesn't handle the save of the <see cref="WorldDefinitionFile"/>: use <see cref="Close(IActivityMonitor)"/>
    /// if the definition file must be saved and committed.
    /// </summary>
    public void Dispose()
    {
        if( _world != null )
        {
            if( _world.DefinitionFile.IsDirty )
            {
                ActivityMonitor.StaticLogger.Warn( "World's DefinitionFile has been modified but not saved." );
            }
            _world.DisposeRepositoriesAndReleasePlugins();
            _world = null;
        }
        _git.Dispose();
    }

}
