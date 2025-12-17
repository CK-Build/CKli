using CK.Core;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
///         Calling <see cref="Clone(IActivityMonitor, ISecretsStore, Uri, bool, in NormalizedPath, bool, string)"/>
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

    /// <summary>
    /// Internal access to the CKliEnv: this is only used to obtain the <see cref="CKliEnv.Committer"/> when
    /// initializing "ckli-repo" tag. CKliEnv must be parameter injected everywhere it is needed for better
    /// maintanability.
    /// </summary>
    internal CKliEnv Context => _context;

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
        return result != CommitResult.Error && _git.Push( monitor, null );
    }

    StackRepository( GitRepository git, in NormalizedPath stackRoot, CKliEnv context, string stackName )
    {
        _git = git;
        _stackRoot = stackRoot;
        _context = context;
        _stackName = stackName;
        var originUrl = git.OriginUrl;
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
                                      gitPath,
                                      gitPath.RemoveFirstPart( gitPath.Parts.Count - 2 ),
                                      isPublic );
        if( git != null )
        {
            var stackRoot = gitPath.RemoveLastPart();
            var url = git.OriginUrl;
            if( CheckOriginUrlStackSuffix( monitor, ref url, out var stackNameFromUrl ) )
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
                    monitor.Error( $"Stack folder '{stackRoot.LastPart}' must be '{stackNameFromUrl}' or '{DuplicatePrefix}{stackNameFromUrl}' (case insensitive) since repository Url is '{git.OriginUrl}'." );
                    error = true;
                }
                if( !error && git.SetCurrentBranch( monitor, stackBranchName, skipPullStack ) )
                {
                    return new StackRepository( git, stackRoot, context, stackNameFromUrl );
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
    /// <returns>The resulting stack repository and world on success. Both are null on error of if no stack is found.</returns>
    public static (StackRepository? Stack, World? World) TryOpenWorldFromPath( IActivityMonitor monitor,
                                                                               CKliEnv context,
                                                                               out bool error,
                                                                               bool skipPullStack = false )
    {
        var stack = TryOpenFromPath( monitor, context, out error, skipPullStack );
        if( stack != null )
        {
            var w = World.Create( monitor, context.Screen.ScreenType, stack, context.CurrentDirectory );
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
    /// <returns>True on success, false on error.</returns>
    public static bool OpenWorldFromPath( IActivityMonitor monitor,
                                          CKliEnv context,
                                          [NotNullWhen( true )] out StackRepository? stack,
                                          [NotNullWhen( true )] out World? world,
                                          bool skipPullStack = false )
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
        world = World.Create( monitor, context.Screen.ScreenType, stack, context.CurrentDirectory );
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
    /// <param name="url">The url of the remote.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    /// <param name="allowDuplicateStack">True to create a "DuplicateOf-XX" stack folder if the stack is already available on this machine.</param>
    /// <param name="stackBranchName">Specifies a branch name. There should be no reason to use multiple branches in a stack repository.</param>
    /// <returns>The repository or null on error.</returns>
    public static StackRepository? Clone( IActivityMonitor monitor,
                                          CKliEnv context,
                                          Uri url,
                                          bool isPublic,
                                          bool allowDuplicateStack = false,
                                          string stackBranchName = "main" )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( context );
        Throw.CheckArgument( context.CurrentStackPath.IsEmptyPath );
        Throw.CheckNotNullArgument( url );
        Throw.CheckNotNullArgument( stackBranchName );
        var parentPath = context.CurrentDirectory;
        if( !parentPath.IsRooted
            || parentPath.Parts.Count < 2
            || parentPath.LastPart.Equals( PublicStackName, StringComparison.OrdinalIgnoreCase )
            || parentPath.LastPart.Equals( PrivateStackName, StringComparison.OrdinalIgnoreCase ) )
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
        // This invariant is... strange but it forces the code above and below to be rigorous.
        Throw.DebugAssert( "IsDuplicate uses ReferenceEquals.",
            (ReferenceEquals( stackRoot.LastPart, stackFolderName ) && stackFolderName != stackNameFromUrl)
            || (ReferenceEquals( stackRoot.LastPart, stackNameFromUrl ) && stackFolderName == stackNameFromUrl) );

        NormalizedPath gitPath = stackRoot.AppendPart( isPublic ? PublicStackName : PrivateStackName );

        var stackGitKey = new GitRepositoryKey( context.SecretsStore, url, isPublic );
        var git = GitRepository.Clone( monitor,
                                       stackGitKey,
                                       gitPath,
                                       gitPath.RemoveFirstPart( gitPath.Parts.Count - 2 ) );
        if( git != null )
        {
            // Before doing anything else, we read the definition file and extract the actual
            // world name with the right casing. If case differ, the git handle is disposed,
            // the folder name is fixed and a new git handle is acquired.
            if( git.SetCurrentBranch( monitor, stackBranchName, skipPullMerge: true )
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
                        if( !FileHelper.TryMoveFolder( monitor, stackRoot, newStackRoot ) )
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
                    }
                }
                SetupNewLocalDirectory( gitPath );
                Registry.RegisterNewStack( monitor, gitPath, url );
                var result = new StackRepository( git, stackRoot, context, stackNameFromUrl );
                if( CloneWorld( monitor, result, result.DefaultWorldName ) )
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
                    !.gitignore
                    """ );
            }
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
                    The url '{gitStack.OriginUrl}' contains a Stack named '{actualStackName}'.
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

    static bool CloneWorld( IActivityMonitor monitor, StackRepository stack, LocalWorldName world )
    {
        var definitionFile = world.LoadDefinitionFile( monitor );
        if( definitionFile == null ) return false;
        var layout = definitionFile.ReadLayout( monitor );
        if( layout == null ) return false;
        bool success = true;
        using( monitor.OpenInfo( $"Cloning {layout.Count} repositories in {stack.StackRoot}." ) )
        {
            foreach( var (url, _, subPath) in layout )
            {
                using( var r = GitRepository.CloneWorkingFolder( monitor,
                                                                 new GitRepositoryKey( stack.SecretsStore, url, stack.IsPublic ),
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

    static bool CheckOriginUrlStackSuffix( IActivityMonitor monitor,
                                           [NotNullWhen( true )] ref Uri? stackUrl,
                                           [NotNullWhen( true )] out string? stackNameFromUrl )
    {
        stackNameFromUrl = null;
        stackUrl = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( monitor, stackUrl, out stackNameFromUrl );
        if( stackUrl == null ) return false;
        Throw.DebugAssert( stackNameFromUrl != null );
        if( stackNameFromUrl.EndsWith( "-Stack", StringComparison.OrdinalIgnoreCase ) && stackNameFromUrl.Length >= 8 )
        {
            stackNameFromUrl = stackNameFromUrl.Substring( 0, stackNameFromUrl.Length - 6 );
            return true;
        }
        monitor.Error( $"The repository Url '{stackUrl}' must have '-Stack' suffix (and the stack name must be at least 2 characters)." );
        return false;
    }
}
