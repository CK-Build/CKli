using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.Core;

public sealed partial class World : IDisposable
{
    readonly StackRepository _stackRepository;
    readonly LocalWorldName _name;
    readonly WorldDefinitionFile _definitionFile;
    readonly IReadOnlyList<(NormalizedPath Path, Uri Uri)> _layout;
    // This caches the GitRepository by uri and path as string and case insensitively.
    // This enables to cache the GitRepository even with case mismatch (that Pull will fix).
    readonly Dictionary<string, GitRepository?> _cachedRepositories;
    GitRepository[]? _allRepositories;

    World( StackRepository stackRepository,
           LocalWorldName name,
           WorldDefinitionFile definitionFile,
           IReadOnlyList<(NormalizedPath Path, Uri Uri)> layout )
    {
        _stackRepository = stackRepository;
        _name = name;
        _definitionFile = definitionFile;
        _layout = layout;
        _cachedRepositories = new Dictionary<string, GitRepository?>( layout.Count, StringComparer.OrdinalIgnoreCase );
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
    /// Finds or creates a cached <see cref="GitRepository"/> from its origin url or working folder path (case insensitive).
    /// <para>
    /// The repository must exist in the <see cref="DefinitionFile"/> or an error is logged and null is returned.
    /// </para>
    /// <para>
    /// If the working folder is not found, this triggers an attempt to fix the layout.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="uriOrPath">The origin url or the working folder path.</param>
    /// <returns>The GitRepository or null on error.</returns>
    public GitRepository? EnsureGitRepository( IActivityMonitor monitor, string uriOrPath ) => TryLoadDefinedGitRepository( monitor, uriOrPath, true );

    /// <summary>
    /// Tries to load all the <see cref="GitRepository"/> in the <see cref="Layout"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The git repositories or null on error.</returns>
    public IReadOnlyList<GitRepository>? EnsureAllGitRepositories( IActivityMonitor monitor )
    {
        if( _allRepositories == null )
        {
            var all = new GitRepository[_layout.Count];
            for( int i = 0; i < _layout.Count; ++i )
            {
                var p = _layout[i].Path;
                if( !_cachedRepositories.TryGetValue( p, out var g ) )
                {
                    g = DoLoadGitRepository( monitor, p, mustExist: true );
                }
                // Stop on error (don't want to fix layout multiple times).
                if( g == null ) return null;
                all[i] = g;
            }
            _allRepositories = all;
        }
        return _allRepositories;
    }

    NormalizedPath FindPath( string key )
    {
        foreach( var (path, uri) in _layout )
        {
            if( path.Path.Equals( key, StringComparison.OrdinalIgnoreCase )
                || uri.ToString().Equals( key, StringComparison.OrdinalIgnoreCase ) )
            {
                return path;
            }
        }
        return default;
    }

    GitRepository? TryLoadDefinedGitRepository( IActivityMonitor monitor, string key, bool mustExist )
    {
        if( _cachedRepositories.TryGetValue( key, out var repository ) && repository == null )
        {
            var p = FindPath( key );
            Throw.DebugAssert( "The key is in the cache: the definition file contains the path or the url.", !p.IsEmptyPath );
            repository = DoLoadGitRepository( monitor, p, mustExist );
        }
        else if( mustExist )
        {
            monitor.Error( $"The world '{_name}' doesn't contain the repository '{key}'." );
        }
        return repository;
    }

    GitRepository? DoLoadGitRepository( IActivityMonitor monitor, NormalizedPath p, bool mustExist )
    {
        GitRepository? repository = GitRepository.Open( monitor, _stackRepository.SecretsStore, p, p.LastPart, _stackRepository.IsPublic );
        if( repository != null )
        {
            _cachedRepositories[p.Path] = repository;
        }
        else if( mustExist )
        {
            using( monitor.OpenWarn( "Missing expected working folder. Trying to fix the repository layout." ) )
            {
                if( FixLayout( monitor, deleteAliens: false, out _ )
                    && _cachedRepositories[p] == null )
                {
                    repository = GitRepository.Open( monitor, _stackRepository.SecretsStore, p, p.LastPart, _stackRepository.IsPublic );
                    if( repository != null )
                    {
                        _cachedRepositories.Add( p, repository );
                        _cachedRepositories.Add( repository.OriginUrl.ToString(), repository );
                    }
                }
            }
        }

        return repository;
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

    public void Dispose()
    {
        foreach( var g in _cachedRepositories.Values )
        {
            g?.Dispose();
        }
    }

    public override string ToString() => _name.ToString();

}
