using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.Core;

/// <summary>
/// The World handles its <see cref="Repo"/> that are defined in its <see cref="Layout"/>.
/// <para>
/// The Repo are loaded on demand and cached. Disposing a World is required to release the
/// internal Repo's <see cref="GitRepository"/>.
/// </para>
/// </summary>
public sealed partial class World : IDisposable
{
    readonly StackRepository _stackRepository;
    readonly LocalWorldName _name;
    readonly WorldDefinitionFile _definitionFile;
    readonly IReadOnlyList<(NormalizedPath Path, Uri Uri)> _layout;
    // This caches the Repo (and its GitRepository) by uri and path as string and case insensitively.
    // This enables to cache the Repo even with case mismatch (that FixLayout will fix).
    readonly Dictionary<string, Repo?> _cachedRepositories;
    Repo[]? _allRepositories;

    World( StackRepository stackRepository,
           LocalWorldName name,
           WorldDefinitionFile definitionFile,
           IReadOnlyList<(NormalizedPath Path, Uri Uri)> layout )
    {
        _stackRepository = stackRepository;
        _name = name;
        _definitionFile = definitionFile;
        _layout = layout;
        _cachedRepositories = new Dictionary<string, Repo?>( layout.Count, StringComparer.OrdinalIgnoreCase );
        foreach( var (path, uri) in layout )
        {
            _cachedRepositories.Add( path, null );
            _cachedRepositories.Add( uri.ToString(), null );
        }
    }

    /// <summary>
    /// Gets the stack that defines this world.
    /// </summary>
    public StackRepository StackRepository => _stackRepository;

    /// <summary>
    /// Gets the world name.
    /// </summary>
    public LocalWorldName Name => _name;

    /// <summary>
    /// Gets the world definition file.
    /// </summary>
    public WorldDefinitionFile DefinitionFile => _definitionFile;

    /// <summary>
    /// Gets the expected layout of this world.
    /// </summary>
    public IReadOnlyList<(NormalizedPath Path, Uri Uri)> Layout => _layout;

    /// <summary>
    /// Calls <see cref="Repo.Pull(IActivityMonitor)"/> on all the repositories of this world.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="skipPull">Optional predicate to not pull some repos.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Pull( IActivityMonitor monitor, Func<Repo,bool>? skipPull = null )
    {
        var all = GetAllDefinedRepo( monitor );
        if( all == null ) return false;
        bool success = true;
        foreach( var g in all )
        {
            if( skipPull == null || skipPull( g ) )
            {
                success &= g.Pull( monitor ).IsSuccess();
            }
        }
        return success;
    }

    /// <summary>
    /// Calls <see cref="Repo.Push(IActivityMonitor)"/> on all the repositories of this world.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="stopOnFirstError">False to continue on errors.</param>
    /// <param name="skipPush">Optional predicate to not push some repos.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Push( IActivityMonitor monitor, bool stopOnFirstError = true, Func<Repo,bool>? skipPush = null )
    {
        var all = GetAllDefinedRepo( monitor );
        if( all == null ) return false;
        bool success = true;
        foreach( var r in all )
        {
            if( skipPush == null || skipPush( r ) )
            {
                success &= r.Push( monitor );
                if( stopOnFirstError && !success )
                {
                    break;
                }
            }
        }
        return success;
    }

    /// <summary>
    /// Gets whether a full path or a origin url is defined in this <see cref="Layout"/>.
    /// The lookup is case insenitive.
    /// </summary>
    /// <param name="uriOrPath">The origin url or a full path that can be below one of the defined <see cref="Repo.WorkingFolder"/>.</param>
    /// <param name="workingFolder">Outputs the non empty <see cref="Repo.WorkingFolder"/> if the repository is defined. The empty path otherwise.</param>
    /// <param name="repo">Outputs the non null <see cref="Repo"/> if it exists and is already loaded.</param>
    /// <returns>True if this repository is defined in this world, false otherwise.</returns>
    public bool FindDefinedRepo( string uriOrPath, out NormalizedPath workingFolder, out Repo? repo )
    {
        if( _cachedRepositories.TryGetValue( uriOrPath, out repo ) && repo != null )
        {
            workingFolder = repo.WorkingFolder;
            return true;
        }
        workingFolder = FindPathOrUriInLayout( uriOrPath );
        return !workingFolder.IsEmptyPath;
    }

    /// <summary>
    /// Finds or creates a cached <see cref="Repo"/> from its origin url or working folder path (case insensitive).
    /// <para>
    /// The repository must exist in the <see cref="DefinitionFile"/> or an error is logged and null is returned.
    /// </para>
    /// <para>
    /// If the working folder is not found, this automatically triggers an attempt to fix the layout.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="uriOrPath">The origin url or the working folder path.</param>
    /// <returns>The Repo or null on error.</returns>
    public Repo? GetDefinedRepo( IActivityMonitor monitor, string uriOrPath ) => TryLoadDefinedGitRepository( monitor, uriOrPath, true );

    /// <summary>
    /// Tries to load all the <see cref="Repo"/> in the <see cref="Layout"/>.
    /// The repositories are cached: this must be called to work with all repositories.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The git repositories or null on error.</returns>
    public IReadOnlyList<Repo>? GetAllDefinedRepo( IActivityMonitor monitor )
    {
        if( _allRepositories == null )
        {
            var all = new Repo[_layout.Count];
            for( int i = 0; i < _layout.Count; ++i )
            {
                var p = _layout[i].Path;
                Repo? g = _cachedRepositories[p]
                          ?? DoLoadGitRepository( monitor, p, mustExist: true );
                // Stop on error (don't want to fix layout multiple times).
                if( g == null ) return null;
                all[i] = g;
            }
            _allRepositories = all;
        }
        return _allRepositories;
    }

    NormalizedPath FindPathOrUriInLayout( string key )
    {
        foreach( var (path, uri) in _layout )
        {
            if( MatchPath( key, path )
                || uri.ToString().Equals( key, StringComparison.OrdinalIgnoreCase ) )
            {
                return path;
            }
        }
        return default;

        static bool MatchPath( string key, NormalizedPath path )
        {
            var p = path.Path;
            return p.StartsWith( key, StringComparison.OrdinalIgnoreCase )
                   && (p.Length == key.Length || key[p.Length] is '/' or '\\');
        }
    }

    Repo? TryLoadDefinedGitRepository( IActivityMonitor monitor, string key, bool mustExist )
    {
        if( FindDefinedRepo( key, out var workingFolder, out var repo ) )
        {
            if( repo == null )
            {
                repo = DoLoadGitRepository( monitor, workingFolder, mustExist );
            }
        }
        else if( mustExist )
        {
            monitor.Error( $"The world '{_name}' doesn't contain the repository '{key}'." );
        }
        return repo;
    }

    Repo? DoLoadGitRepository( IActivityMonitor monitor, NormalizedPath p, bool mustExist )
    {
        Repo? repo = null;
        if( Directory.Exists( p ) )
        {
            var repository = GitRepository.Open( monitor, _stackRepository.SecretsStore, p, p.LastPart, _stackRepository.IsPublic );
            if( repository != null )
            {
                repo = new Repo( this, repository );
                _cachedRepositories[p.Path] = repo;
                _cachedRepositories[repository.OriginUrl.ToString()] = repo;
            }
        }
        if( repo == null && mustExist )
        {
            using( monitor.OpenWarn( $"""
                Missing expected working folder '{p}'.
                Trying to fix the repository layout.
                """ ) )
            {
                if( FixLayout( monitor, deleteAliens: false, out _ )
                    && (repo = _cachedRepositories[p]) == null )
                {
                    var repository = GitRepository.Open( monitor, _stackRepository.SecretsStore, p, p.LastPart, _stackRepository.IsPublic );
                    if( repository != null )
                    {
                        repo = new Repo( this, repository );
                        _cachedRepositories[p] = repo;
                        _cachedRepositories[repo.OriginUrl.ToString()] = repo;
                    }
                }
            }
        }
        return repo;
    }

    /// <summary>
    /// Tries to create a world from a <paramref name="path"/> that must start with the <see cref="StackRepository.StackRoot"/>.
    /// <para>
    /// This tries to find a LTS world if the path is below StackRoot and its first folder starts with '@' and is
    /// a valid LTS name. When the path is every where else, the default world is returned.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="path">The path that must be or start with <see cref="StackRepository.StackRoot"/> or an <see cref="ArgumentException"/> is thrown.</param>
    /// <returns>The world or null on error.</returns>
    public static World? TryOpenFromPath( IActivityMonitor monitor, StackRepository stackRepository, NormalizedPath path )
    {
        var worldName = stackRepository.GetWorldNameFromPath( monitor, path );
        var definitionFile = worldName?.LoadDefinitionFile( monitor );
        var layout = definitionFile?.ReadLayout( monitor );
        if( layout == null )
        {
            return null;
        }
        return new World( stackRepository, worldName!, definitionFile!, layout );
    }

    /// <summary>
    /// Releases all the <see cref="Repo"/>'s internal <see cref="GitRepository"/>.
    /// </summary>
    public void Dispose()
    {
        foreach( var g in _cachedRepositories.Values )
        {
            g?._git.Dispose();
        }
        _cachedRepositories.Clear();
        _allRepositories = null;
    }

    /// <inheritdoc cref="WorldName.ToString"/>
    public override string ToString() => _name.ToString();

}
