using CK.Core;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
///         Calling <see cref="Clone(IActivityMonitor, ISecretsStore, Uri, bool, in NormalizedPath, bool, string)"/>
///         from the remote Uri of the stack.
///     </item>
/// </list>
/// </para>
/// </summary>
public sealed partial class StackRepository : IDisposable
{
    public const string PublicStackName = ".PublicStack";
    public const string PrivateStackName = ".PrivateStack";
    public const string DuplicatePrefix = "DuplicateOf-";

    readonly GitRepository _git;
    readonly NormalizedPath _stackRoot;
    readonly ISecretsStore _secretsStore;
    readonly NormalizedPath _localProxyRepositoriesPath;
    // No DuplicatePrefix here.
    readonly string _stackName;
    LocalWorldName? _defaultWorldName;
    ImmutableArray<LocalWorldName> _worldNames;
    World? _world;

    /// <summary>
    /// Gets the root path of the stack (the parent folder of the ".PrivateStack" or ".PublicStack" folder).
    /// </summary>
    public NormalizedPath StackRoot => _stackRoot;

    /// <summary>
    /// Gets the path of the ".PrivateStack" or ".PublicStack". 
    /// </summary>
    public NormalizedPath StackWorkingFolder => _git.WorkingFolder;

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
    public bool IsPublic => _git.IsPublic;

    /// <summary>
    /// Gets the stack's repository url.
    /// </summary>
    public Uri OriginUrl => _git.OriginUrl;

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
    public ISecretsStore SecretsStore => _secretsStore;

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
    /// and lexigraphically sorted.
    /// <para>
    /// Worlds are defined by the xml World definition files "StackName[@LTSName].xml" in this stack reposiory <see cref="StackWorkingFolder"/>.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The name or null on error.</returns>
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
    /// Gets the default world if it exists or emits an error if it doesn't.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The default world if it exists.</returns>
    public LocalWorldName? GetDefaultWorldName( IActivityMonitor monitor )
    {
        var defaultWorld = WorldNames.FirstOrDefault( w => w.LTSName == null );
        if( defaultWorld == null )
        {
            monitor.Error( $"Stack '{StackRoot}': the default World definition is missing. Expecting file '{_git.WorkingFolder}/{StackName}.xml'." );
        }
        return defaultWorld;
    }

    /// <summary>
    /// Gets the world from a <paramref name="path"/> that must start with <see cref="StackRoot"/>.
    /// <para>
    /// This tries to find a LTS world if the path is below StackRoot and its first folder starts with '@' and is
    /// a valid LTS name. When the path is everywhere else, <see cref="GetDefaultWorldName(IActivityMonitor)"/> is used.
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
        return GetDefaultWorldName( monitor );
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
    /// Commits and push changes to the remote.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool PushChanges( IActivityMonitor monitor )
    {
        CommitResult result = _git.Commit( monitor, "Automatic pre-push commit." );
        if( result == CommitResult.NoChanges )
        {
            monitor.Trace( "Nothing committed. Skipping push." );
            return true;
        }
        return result != CommitResult.Error && _git.Push( monitor );
    }

    StackRepository( GitRepository git, in NormalizedPath stackRoot, ISecretsStore secretsStore, string stackName )
    {
        _git = git;
        _stackRoot = stackRoot;
        _secretsStore = secretsStore;
        _stackName = stackName;
        var originUrl = git.OriginUrl;
        if( originUrl.IsFile )
        {
            var p  = new NormalizedPath( originUrl.LocalPath );
            if( p.Parts.Count > 1 )
            {
                _localProxyRepositoriesPath = p.RemoveLastPart();
            }
        }
    }

    /// <summary>
    /// Tries to open a stack directory from a path.
    /// This lookups the ".PrivateStack" or ".PublicStack" in and above <paramref name="path"/>: if none
    /// are found, there is no <paramref name="error"/> and null is returned.
    /// <para>
    /// On success, the stack repository is on the <paramref name="stackBranchName"/> and, by default,
    /// the stack and its world definitions has been updated (but not the repositories of any of the worlds):
    /// use <paramref name="skipPullStack"/> to leave the local stack untouched.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secret key store.</param>
    /// <param name="path">The starting path.</param>
    /// <param name="error">True on errror, false on success.</param>
    /// <param name="skipPullStack">True to leave the stack repository as-is. By default, a pull is done from the remote stack repository.</param>
    /// <param name="stackBranchName">Specifies a branch name. There should be no reason to use multiple branches in a stack repository.</param>
    /// <returns>The resulting stack repository if found and opened successfully. May be null if not found.</returns>
    public static StackRepository? TryOpenFromPath( IActivityMonitor monitor,
                                                    ISecretsStore secretsStore,
                                                    in NormalizedPath path,
                                                    out bool error,
                                                    bool skipPullStack = false,
                                                    string stackBranchName = "main" )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( stackBranchName );
        error = false;
        var gitPath = FindGitStackPath( path );
        if( gitPath.IsEmptyPath ) return null;

        var isPublic = gitPath.LastPart == PublicStackName;
        var git = GitRepository.Open( monitor,
                                      secretsStore,
                                      gitPath,
                                      gitPath.RemoveFirstPart( gitPath.Parts.Count - 2 ),
                                      isPublic );
        if( git != null )
        {
            var stackRoot = gitPath.RemoveLastPart();
            var url = git.OriginUrl;
            if( CheckOriginUrlStackSuffix( monitor, ref url, out var stackNameFromUrl ) )
            {
                if( stackRoot.LastPart == stackNameFromUrl )
                {
                    // Use the same reference for non duplicate: ReferenceEquals is used.
                    stackNameFromUrl = stackRoot.LastPart;
                }
                else if( stackRoot.LastPart != DuplicatePrefix + stackNameFromUrl )
                {
                    monitor.Error( $"Stack folder '{stackRoot.LastPart}' must be '{stackNameFromUrl}' (or '{DuplicatePrefix}{stackNameFromUrl}') since repository Url is '{git.OriginUrl}'." );
                    error = true;
                }
                if( !error && git.SetCurrentBranch( monitor, stackBranchName, skipPullStack ) )
                {
                    return new StackRepository( git, stackRoot, secretsStore, stackNameFromUrl );
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
    /// This lookups the ".PrivateStack" or ".PublicStack" in and above <paramref name="path"/>: a stack
    /// must be found otherwise it is an error.
    /// <para>
    /// On success, the stack repository is on the <paramref name="stackBranchName"/> and, by default,
    /// the stack and its world definitions has been updated (but not the repositories of any of the worlds):
    /// use <paramref name="skipPullStack"/> to leave the local stack untouched.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secret key store.</param>
    /// <param name="path">The starting path.</param>
    /// <param name="stack">The non null stack on success.</param>
    /// <param name="skipPullStack">True to leave the stack repository as-is. By default, a pull is done from the remote stack repository.</param>
    /// <param name="stackBranchName">Specifies a branch name. There should be no reason to use multiple branches in a stack repository.</param>
    /// <returns>The resulting stack repository if found and opened successfully. May be null if not found.</returns>
    public static bool OpenFromPath( IActivityMonitor monitor,
                                     ISecretsStore secretsStore,
                                     in NormalizedPath path,
                                     [NotNullWhen(true)] out StackRepository? stack,
                                     bool skipPullStack = false,
                                     string stackBranchName = "main" )
    {
        stack = TryOpenFromPath( monitor, secretsStore, path, out bool error, skipPullStack, stackBranchName );
        if( error )
        {
            Throw.DebugAssert( stack == null );
            return false;
        }
        if( stack == null )
        {
            monitor.Error( $"Unable to find a stack repository from path '{path}'." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Tries to open a stack and a world from a <paramref name="path"/>. If no stack is found on or above the path,
    /// this is not an error but <c>(null,null)</c> is returned.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secret key store.</param>
    /// <param name="path">The starting path.</param>
    /// <param name="error">True on error, false on success.</param>
    /// <param name="skipPullStack">True to leave the stack repository as-is. By default, a pull is done from the remote stack repository.</param>
    /// <returns>The resulting stack repository and world on success. Both are null on error of if no stack is found.</returns>
    public static (StackRepository? Stack, World? World) TryOpenWorldFromPath( IActivityMonitor monitor,
                                                                               ISecretsStore secretsStore,
                                                                               in NormalizedPath path,
                                                                               out bool error,
                                                                               bool skipPullStack = false )
    {
        var stack = TryOpenFromPath( monitor, secretsStore, path, out error, skipPullStack );
        if( stack != null )
        {
            var w = World.Create( monitor, stack, path );
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
    /// Opens a stack and a world from a <paramref name="path"/>.
    /// A stack must be found on or above the path otherwise it is an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secret key store.</param>
    /// <param name="path">The starting path.</param>
    /// <param name="stack">The non null stack on success.</param>
    /// <param name="world">The non null world on success.</param>
    /// <param name="skipPullStack">True to leave the stack repository as-is. By default, a pull is done from the remote stack repository.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool OpenWorldFromPath( IActivityMonitor monitor,
                                          ISecretsStore secretsStore,
                                          in NormalizedPath path,
                                          [NotNullWhen( true )] out StackRepository? stack,
                                          [NotNullWhen( true )] out World? world,
                                          bool skipPullStack = false )
    {
        world = null;
        stack = TryOpenFromPath( monitor, secretsStore, path, out bool error, skipPullStack );
        if( error )
        {
            return false;
        }
        if( stack == null )
        {
            monitor.Error( $"No stack found for path '{path}'." );
            return false;
        }
        world = World.Create( monitor, stack, path );
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
    /// <param name="secretsStore">The secret key store.</param>
    /// <param name="url">The url of the remote.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    /// <param name="parentPath">The path where the root stack folder will be created. Must be rooted.</param>
    /// <param name="allowDuplicateStack">True to create a "DuplicateOf-XX" stack folder if the stack is already available on this machine.</param>
    /// <param name="stackBranchName">Specifies a branch name. There should be no reason to use multiple branches in a stack repository.</param>
    /// <returns>The repository or null on error.</returns>
    public static StackRepository? Clone( IActivityMonitor monitor,
                                          ISecretsStore secretsStore,
                                          Uri url,
                                          bool isPublic,
                                          in NormalizedPath parentPath,
                                          bool allowDuplicateStack = false,
                                          string stackBranchName = "main" )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( secretsStore );
        Throw.CheckNotNullArgument( url );
        Throw.CheckNotNullArgument( stackBranchName );
        if( !parentPath.IsRooted || parentPath.Parts.Count < 2 || parentPath.LastPart == PublicStackName || parentPath.LastPart == PrivateStackName )
        {
            monitor.Error( $"Invalid path '{parentPath}': it must be rooted and not end with {PublicStackName} or {PrivateStackName}." );
        }
        if( !CheckOriginUrlStackSuffix( monitor, ref url!, out var stackNameFromUrl ) )
        {
            return null;
        }

        // Default folder name is the stackNameFromUrl.
        var stackFolderName = stackNameFromUrl;
        var already = Registry.CheckExistingStack( monitor, url );
        if( already.Count > 0 )
        {
            monitor.Log( allowDuplicateStack ? LogLevel.Warn : LogLevel.Error,
                        $"""
                        The stack '{stackNameFromUrl}' at '{url}' is already available here:
                        {already.Select( p => p.Path ).Concatenate( Environment.NewLine )}
                        """ );
            if( !allowDuplicateStack ) return null;
            stackFolderName = DuplicatePrefix + stackNameFromUrl;
        }
        var stackRoot = parentPath.AppendPart( stackFolderName );

        // Secure Stack inside Stack scenario.
        var parentStack = FindGitStackPath( parentPath );
        if( !parentStack.IsEmptyPath )
        {
            var stackAbove = parentStack.RemoveLastPart();
            var safeRoot = stackAbove.RemoveLastPart().AppendPart( stackFolderName );
            monitor.Warn( $"Resolved stack path '{stackRoot}' is inside stack '{stackAbove}': moving it to {safeRoot}." );
            stackRoot = safeRoot;
        }

        // Don't clone if the resolved path exists.
        if( Path.Exists( stackRoot ) )
        {
            monitor.Error( $"The resolved path to clone '{stackRoot}' already exists." );
            return null;
        }

        // The NormalizedPath keeps (MUST keep!) the LastPart reference.
        Throw.DebugAssert( "IsDuplicate uses ReferenceEquals.",
            (ReferenceEquals( stackRoot.LastPart, stackFolderName ) && stackFolderName != stackNameFromUrl)
            || (ReferenceEquals( stackRoot.LastPart, stackNameFromUrl ) && stackFolderName == stackNameFromUrl) );

        NormalizedPath gitPath = stackRoot.AppendPart( isPublic ? PublicStackName : PrivateStackName );

        var git = GitRepository.Clone( monitor,
                                       new GitRepositoryKey( secretsStore, url, isPublic ),
                                       gitPath,
                                       gitPath.RemoveFirstPart( gitPath.Parts.Count - 2 ) );
        if( git != null )
        {
            if( git.SetCurrentBranch( monitor, stackBranchName ) )
            {
                SetupNewLocalDirectory( gitPath );
                Registry.RegisterNewStack( monitor, gitPath, url );
                var result = new StackRepository( git, stackRoot, secretsStore, stackNameFromUrl );
                if( CloneWorld( monitor, result, secretsStore, result.DefaultWorldName ) )
                {
                    return result;
                }
                result.Dispose();
            }
            git.Dispose();
            return null;
        }
        return null;

        static void SetupNewLocalDirectory( NormalizedPath gitPath )
        {
            // Ensures that the $Local directory is created.
            var localDir = gitPath.AppendPart( "$Local" );
            if( !Directory.Exists( localDir ) )
            {
                // The .gitignore ignores it. It is created only once.
                Directory.CreateDirectory( localDir );
                var ignore = gitPath.AppendPart( ".gitignore" );
                if( !File.Exists( ignore ) ) File.WriteAllText( ignore, """
                    $Local/
                    Logs/
                    .vs/
                    .idea/

                    """ );
            }
        }
    }

    static bool CloneWorld( IActivityMonitor monitor, StackRepository stack, ISecretsStore secretsStore, LocalWorldName world )
    {
        var definitionFile = world.LoadDefinitionFile( monitor );
        if( definitionFile == null ) return false;
        var layout = definitionFile.ReadLayout( monitor );
        if( layout == null ) return false;
        bool success = true;
        using( monitor.OpenInfo( $"Cloning {layout.Count} repositories in {stack.StackRoot}." ) )
        {
            foreach( var (subPath, url) in layout )
            {
                using( var r = GitRepository.CloneWorkingFolder( monitor,
                                                                 new GitRepositoryKey( secretsStore, url, stack.IsPublic ),
                                                                 world.WorldRoot.Combine( subPath ) ) )
                {
                    success &= r != null;
                }
            }
        }
        return success;
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
        content.SaveWithoutXmlDeclaration( newOne.XmlDescriptionFilePath );
        Directory.CreateDirectory( newOne.WorldRoot );
        return newOne;
    }

    public void Dispose()
    {
        _git.Dispose();
        _world?.DisposeRepositoriesAndPlugins();
    }

    static bool CheckOriginUrlStackSuffix( IActivityMonitor monitor,
                                           [NotNullWhen(true)]ref Uri? stackUrl,
                                           [NotNullWhen(true)]out string? stackNameFromUrl )
    {
        stackNameFromUrl = null;
        stackUrl = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( monitor, stackUrl, out stackNameFromUrl );
        if( stackUrl == null ) return false;
        Throw.DebugAssert( stackNameFromUrl != null );
        if( stackNameFromUrl.EndsWith( "-Stack" ) && stackNameFromUrl.Length >= 8 )
        {
            stackNameFromUrl = stackNameFromUrl.Substring( 0, stackNameFromUrl.Length - 6 );
            return true;
        }
        monitor.Error( $"The repository Url '{stackUrl}' must have '-Stack' suffix (and the stack name must be at least 2 characters)." );
        return false;
    }
}
