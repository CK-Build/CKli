using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Encapsulate the <see cref="GitRepository"/> with its worlds.
/// There are only 2 ways to obtain a StackRepository:
/// <list type="bullet">
///     <item>Calling <see cref="TryOpenFrom(IActivityMonitor, ISecretsStore, in NormalizedPath, out CKli.Core.StackRepository?, string)"/> from any local path.</item>
///     <item>Calling <see cref="Clone(IActivityMonitor, ISecretsStore, Uri, bool, in NormalizedPath, bool, string)"/> from the remote Uri of the stack.</item>
/// </list>
/// </summary>
public sealed partial class StackRepository : IDisposable
{
    public const string PublicStackName = ".PublicStack";
    public const string PrivateStackName = ".PrivateStack";
    public const string DuplicatePrefix = "DuplicateOf-";

    readonly GitRepository _git;
    readonly NormalizedPath _stackRoot;
    LocalWorldName? _defaultWorldName;

    ImmutableArray<LocalWorldName> _worldNames;
    bool _isDirty;

    /// <summary>
    /// Gets the root path of the stack.
    /// </summary>
    public NormalizedPath StackRoot => _stackRoot;

    /// <summary>
    /// Gets the path of the ".PrivateStack" or ".PublicStack". 
    /// </summary>
    public NormalizedPath Path => _git.FullPhysicalPath;

    /// <summary>
    /// Gets the name of this stack that is necessarily the last part of the <see cref="StackRoot"/>.
    /// </summary>
    public string StackName => _stackRoot.LastPart;

    /// <summary>
    /// Gets the branch name: Should always be <see cref="IWorldName.MasterName"/> but this may be changed.
    /// </summary>
    public string BranchName => _git.CurrentBranchName;

    /// <summary>
    /// Gets whether this stack is dirty.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Gets whether this stack is public.
    /// </summary>
    public bool IsPublic => _git.IsPublic;

    /// <summary>
    /// Gets whether the stack's repository url.
    /// </summary>
    public Uri OriginUrl => _git.OriginUrl;

    /// <summary>
    /// Gets the default world name (no <see cref="WorldName.LTSName"/>).
    /// </summary>
    public LocalWorldName DefaultWorldName
    {
        get
        {
            if( _defaultWorldName == null )
            {
                _defaultWorldName = new LocalWorldName( StackName, null, _stackRoot, Path.AppendPart( $"{StackName}.xml" ) );
            }
            return _defaultWorldName;
        }
    }

    /// <summary>
    /// Gets all the worlds that this stack contains starting with the <see cref="DefaultWorldName"/>
    /// and lexigraphically sorted.
    /// </summary>
    public ImmutableArray<LocalWorldName> WorldNames
    {
        get
        {
            if( _worldNames.IsDefault )
            {
                _worldNames = Directory.GetFiles( Path, $"{StackName}.*.xml" )
                                .Select( p => LocalWorldName.TryParseDefinitionFilePath( p ) )
                                .Where( w => w != null )
                                .OrderBy( n => n!.FullName )
                                .Prepend( DefaultWorldName )
                                .ToImmutableArray()!;
            }
            return _worldNames;
        }
    }

    /// <summary>
    /// Gets the default world if it exists or emits an error if it doesn't.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The default world if it exists.</returns>
    public LocalWorldName? GetDefaultWorld( IActivityMonitor monitor )
    {
        var defaultWorld = _worldNames.FirstOrDefault( w => w.LTSName == null );
        if( defaultWorld == null )
        {
            monitor.Error( $"Stack '{StackRoot}': the default World definition is missing. Expecting file '{_git.FullPhysicalPath}/{StackName}.xml'." );
        }
        return defaultWorld;
    }

    /// <summary>
    /// Commits and push changes to the remote.
    /// Nothing is done if <see cref="IsDirty"/> is false.
    /// </summary>
    /// <param name="m">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    internal bool PushChanges( IActivityMonitor m )
    {
        if( !_isDirty ) return false;
        CommitResult result = _git.Commit( m, "Automatic synchronization commit." );
        if( result == CommitResult.NoChanges )
        {
            m.Trace( "Nothing committed. Skipping push." );
            _isDirty = false;
        }
        else
        {
            _isDirty = result == CommitResult.Error || !_git.Push( m );
        }
        return !_isDirty;
    }

    StackRepository( GitRepository git, in NormalizedPath stackRoot )
    {
        _git = git;
        _stackRoot = stackRoot;
    }

    /// <summary>
    /// Tries to open a stack directory from a path.
    /// This lookups the ".PrivateStack" or ".PublicStack" in and above <paramref name="path"/>: if none
    /// are found, this is successful but <paramref name="stackRepository"/> is null.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secret key store.</param>
    /// <param name="path">The starting path.</param>
    /// <param name="stackRepository">The resulting stack directory if found and opened successfully.</param>
    /// <param name="stackBranchName">Specifies a branch name. There should be no reason to use multiple branch in a stack repository.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryOpenFrom( IActivityMonitor monitor,
                                    ISecretsStore secretsStore,
                                    in NormalizedPath path,
                                    out StackRepository? stackRepository,
                                    string stackBranchName = "main" )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( stackBranchName );
        stackRepository = null;
        var gitPath = FindGitStackPath( path );
        if( gitPath.IsEmptyPath ) return true;

        var isPublic = gitPath.LastPart == PublicStackName;
        var git = GitRepository.Open( monitor,
                                      secretsStore,
                                      gitPath,
                                      gitPath,
                                      isPublic );
        if( git != null )
        {
            var stackRoot = gitPath.RemoveLastPart();
            var url = git.OriginUrl;
            if( CheckOriginUrlStackSuffix( monitor, ref url, out var stackNameFromUrl ) )
            {
                if( stackRoot.LastPart != stackNameFromUrl
                    && stackRoot.LastPart != DuplicatePrefix + stackNameFromUrl )
                {
                    monitor.Error( $"Stack folder '{stackRoot.LastPart}' must be '{stackNameFromUrl}' (or '{DuplicatePrefix}{stackNameFromUrl}') since repository Url is '{git.OriginUrl}'." );
                }
                else if( git.SetCurrentBranch( monitor, stackBranchName, skipPullMerge: true ) )
                {
                    stackRepository = new StackRepository( git, stackRoot );
                    return true;
                }
            }
            git.Dispose();
        }
        return false;
    }

    /// <summary>
    /// Ensures that a root stack directory exists. The ".PrivateStack" or ".PublicStack" is checked out if needed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secretsStore">The secret key store.</param>
    /// <param name="url">The url of the remote.</param>
    /// <param name="isPublic">Whether this repository is public.</param>
    /// <param name="parentPath">The path where the root stack folder will be created. Must be rooted.</param>
    /// <param name="allowDuplicateStack">True to create a "DuplicateOf-XX" stack folder if the stack is already available on this machine.</param>
    /// <param name="stackBranchName">Specifies a branch name. There should be no reason to use multiple branch in a stack repository.</param>
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

        var already = Registry.CheckExistingStack( monitor, url );
        if( already.Count > 0 )
        {
            monitor.Log( allowDuplicateStack ? LogLevel.Warn : LogLevel.Error,
                        $"""
                        The stack '{stackNameFromUrl}' at '{url}' is already available here:
                        {already.Select( p => p.Path ).Concatenate( Environment.NewLine )}
                        """ );
            if( !allowDuplicateStack ) return null;
            stackNameFromUrl = DuplicatePrefix + stackNameFromUrl;
        }

        var stackRoot = parentPath.AppendPart( stackNameFromUrl );

        // Secure Stack inside Stack scenario.
        var parentStack = FindGitStackPath( parentPath );
        if( !parentStack.IsEmptyPath )
        {
            var stackAbove = parentStack.RemoveLastPart();
            var safeRoot = stackAbove.RemoveLastPart().AppendPart( stackNameFromUrl );
            monitor.Warn( $"Resolved stack path '{stackRoot}' is inside stack '{stackAbove}': moving it to {safeRoot}." );
            stackRoot = safeRoot;
        }

        // Don't clone if the resolved path exists.
        if( System.IO.Path.Exists( stackRoot ) )
        {
            monitor.Error( $"The resolved path to clone '{stackRoot}' already exists." );
            return null;
        }

        NormalizedPath gitPath = stackRoot.AppendPart( isPublic ? PublicStackName : PrivateStackName );

        var git = GitRepository.Clone( monitor,
                                       new GitRepositoryKey( secretsStore, url, isPublic ),
                                       gitPath,
                                       gitPath );
        if( git != null )
        {
            if( git.SetCurrentBranch( monitor, stackBranchName ) )
            {
                SetupLocalDirectory( gitPath );
                var result = new StackRepository( git, stackRoot );
                if( CloneWorld( monitor, result, secretsStore, result.DefaultWorldName ) )
                {
                    return result;
                }
                result.Dispose();
            }
            return null;
        }
        git.Dispose();
        return null;

        static void SetupLocalDirectory( NormalizedPath gitPath )
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
                var r = GitRepository.CloneWorkingFolder( monitor,
                                                          new GitRepositoryKey( secretsStore, url, stack.IsPublic ),
                                                          world.Root.Combine( subPath ) );
                if( r == null )
                {
                    success = false;
                }
                else
                {
                    r.Dispose();
                }
            }
        }
        return success;
    }

    /// <summary>
    /// Finds a git stack ".PrivateStack" or ".PublicStack" above. 
    /// </summary>
    /// <param name="path">Starting path.</param>
    /// <returns>A git stack or the empty path.</returns>
    public static NormalizedPath FindGitStackPath( NormalizedPath path )
    {
        foreach( var tryPath in path.PathsToFirstPart( null, new[] { PublicStackName, PrivateStackName } ) )
        {
            if( Directory.Exists( tryPath ) ) return tryPath;
        }
        return default;
    }

    public LocalWorldName? CreateNewLTS( IActivityMonitor monitor, string ltsName, XDocument content )
    {
        Throw.CheckArgument( content?.Root != null );
        Throw.CheckNotNullOrWhiteSpaceArgument( ltsName );

        var newRoot = _stackRoot.AppendPart( ltsName );
        var newDesc = _git.FullPhysicalPath.AppendPart( $"{StackName}{ltsName}.xml" );
        var newOne = new LocalWorldName( StackName, ltsName, newRoot, newDesc );

        if( File.Exists( newOne.XmlDescriptionFilePath ) )
        {
            monitor.Error( $"Unable to create '{newOne}' world: file '{newOne.XmlDescriptionFilePath}' already exists." );
            return null;
        }
        if( Directory.Exists( newOne.Root ) )
        {
            monitor.Error( $"Unable to create '{newOne}' world: directory {newOne.Root} already exists." );
            return null;
        }
        content.Save( newOne.XmlDescriptionFilePath );
        Directory.CreateDirectory( newOne.Root );
        _isDirty = true;
        return newOne;
    }

    public void Dispose()
    {
        _git.Dispose();
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
