using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.Core;

/// <summary>
/// The World handles the <see cref="Repo"/> that are defined in its <see cref="Layout"/>.
/// <para>
/// The only way to obtain a World is to use the StackRepository.
/// </para>
/// </summary>
public sealed partial class World
{
    /// <summary>
    /// Gets the <see cref="InformationalVersion"/> of this assembly.
    /// </summary>
    public static readonly InformationalVersion CKliVersion = InformationalVersion.ReadFromAssembly( typeof( PluginMachinery ).Assembly );

    static Func<IActivityMonitor, NormalizedPath, PluginCollectorContext, IPluginCollection?>? _pluginLoader;

    /// <summary>
    /// Gets or sets once the loader for plugins.
    /// <para>
    /// When not set, plugins are disabled. Once set, it cannot be changed.
    /// </para>
    /// </summary>
    public static Func<IActivityMonitor, NormalizedPath, PluginCollectorContext, IPluginCollection?>? PluginLoader
    {
        get => _pluginLoader;
        set
        {
            Throw.CheckState( "One set, PluginLoader cannot be changed.", _pluginLoader == null || value != _pluginLoader );
            _pluginLoader = value;
        }
    }

    readonly StackRepository _stackRepository;
    readonly LocalWorldName _name;
    readonly WorldDefinitionFile _definitionFile;
    readonly WorldEvents _events;
    readonly PluginMachinery? _pluginMachinery;
    IDisposable? _instantiatedPlugins;

    // The WorldDefinitionFile maintains its layout list.
    // AddRepository, RemoveRepository and XifLayout are the only ones that can
    // update this layout.
    readonly IReadOnlyList<(NormalizedPath Path, Uri Uri)> _layout;

    // This caches the Repo (and its GitRepository) by uri and path as string and case insensitively.
    // This enables to cache the Repo even with case mismatch (that FixLayout will fix).
    readonly Dictionary<string, Repo?> _cachedRepositories;
    Repo[]? _allRepositories;
    Repo? _firstRepo;

    World( StackRepository stackRepository,
           LocalWorldName name,
           WorldDefinitionFile definitionFile,
           IReadOnlyList<(NormalizedPath Path, Uri Uri)> layout,
           PluginMachinery? pluginMachinery )
    {
        _stackRepository = stackRepository;
        _name = name;
        _definitionFile = definitionFile;
        _layout = layout;
        _pluginMachinery = pluginMachinery;
        _cachedRepositories = new Dictionary<string, Repo?>( layout.Count, StringComparer.OrdinalIgnoreCase );
        foreach( var (path, uri) in _layout )
        {
            _cachedRepositories.Add( path, null );
            _cachedRepositories.Add( uri.ToString(), null );
        }
        _events = new WorldEvents();
    }

    /// <summary>
    /// Only called by the StackRepository.
    /// Fails if the world layout cannot be loaded or plugins initialization fails (null is returned).
    /// </summary>
    internal static World? Create( IActivityMonitor monitor, StackRepository stackRepository, NormalizedPath path )
    {
        var worldName = stackRepository.GetWorldNameFromPath( monitor, path );
        var definitionFile = worldName?.LoadDefinitionFile( monitor );
        var layout = definitionFile?.ReadLayout( monitor );
        if( layout == null )
        {
            return null;
        }
        Throw.DebugAssert( worldName != null && definitionFile != null );
        PluginMachinery? machinery = null;
        if( !definitionFile.IsPluginsDisabled && World.PluginLoader != null )
        {
            machinery = PluginMachinery.Create( monitor, worldName, definitionFile );
            if( machinery == null ) return null;
        }
        else
        {
            monitor.Info( World.PluginLoader == null
                            ? "Plugins are disabled because there is no configured World.PluginLoader."
                            : $"Plugins are disabled by configuration in '{worldName.FullName}.xml'." );
        }
        var w = new World( stackRepository, worldName, definitionFile, layout, machinery );
        if( machinery != null )
        {
            if( !w.InstantiatePlugins( monitor ) )
            {
                w.DisposeRepositoriesAndPlugins();
                w = null;
            }
        }
        return w;
    }

    bool InstantiatePlugins( IActivityMonitor monitor )
    {
        Throw.DebugAssert( _pluginMachinery != null );
        try
        {
            _instantiatedPlugins = _pluginMachinery.WorldPlugins.Create( this );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While instantiating plugins.", ex );
            return false;
        }
    }

    internal void DisposeRepositoriesAndPlugins()
    {
        _events.ReleaseEvents();
        if( _pluginMachinery != null )
        {
            Throw.DebugAssert( _instantiatedPlugins != null );
            _instantiatedPlugins.Dispose();
            _instantiatedPlugins = null;
            _pluginMachinery.ReleasePlugins();
        }
        var r = _firstRepo;
        while( r != null )
        {
            r._git.Dispose();
            r = r._nextRepo;
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
    /// Gets all events that can be raised by a World.
    /// </summary>
    public WorldEvents Events => _events;

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
    /// Calls <see cref="Repo.Fetch(IActivityMonitor, bool)"/> on all the repositories of this world.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="originOnly">False to fetch all the remote branches. By default, branches from only 'origin' remote are considered.</param>
    /// <param name="skipFetch">Optional predicate to not fetch some repos.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Fetch( IActivityMonitor monitor, bool originOnly = true, Func<Repo,bool>? skipFetch = null )
    {
        var all = GetAllDefinedRepo( monitor );
        if( all == null ) return false;
        bool success = true;
        foreach( var g in all )
        {
            if( skipFetch == null || skipFetch( g ) )
            {
                success &= g.Fetch( monitor, originOnly );
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
        return FindDefinedRepo( uriOrPath, out workingFolder, out repo, out _ );
    }

    bool FindDefinedRepo( string uriOrPath, out NormalizedPath workingFolder, out Repo? repo, out int index )
    {
        if( _cachedRepositories.TryGetValue( uriOrPath, out repo ) && repo != null )
        {
            workingFolder = repo.WorkingFolder;
            index = repo.Index;
            return true;
        }
        workingFolder = FindPathOrUriInLayout( uriOrPath, out index );
        return !workingFolder.IsEmptyPath;
    }

    /// <summary>
    /// Finds or creates a cached <see cref="Repo"/> from its origin url or a path (case insensitive).
    /// <para>
    /// The repository must exist in the <see cref="DefinitionFile"/> or an error is logged and null is returned.
    /// </para>
    /// <para>
    /// If the repository is defined and the working folder is not found, this automatically triggers an attempt to fix the layout.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="uriOrPath">The origin url or or a full path that can be below one of the defined <see cref="Repo.WorkingFolder"/>.</param>
    /// <returns>The Repo or null on error.</returns>
    public Repo? GetDefinedRepo( IActivityMonitor monitor, string uriOrPath ) => TryLoadDefinedGitRepository( monitor, uriOrPath, true );

    /// <summary>
    /// Tries to finds or creates a cached <see cref="Repo"/> from its origin url or a path (case insensitive).
    /// If no Repo is found in this <see cref="Layout"/>, null is returned and this is not an error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="uriOrPath">The origin url or or a full path that can be below one of the defined <see cref="Repo.WorkingFolder"/>.</param>
    /// <returns>The Repo or null if not not found.</returns>
    public Repo? TryGetRepo( IActivityMonitor monitor, string uriOrPath ) => TryLoadDefinedGitRepository( monitor, uriOrPath, false );

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
                          ?? DoLoadGitRepository( monitor, p, i, mustExist: true );
                // Stop on error (don't want to fix layout multiple times).
                if( g == null ) return null;
                all[i] = g;
            }
            _allRepositories = all;
        }
        return _allRepositories;
    }

    NormalizedPath FindPathOrUriInLayout( string key, out int index )
    {
        for( index = 0; index < _layout.Count; ++index )
        {
            var (path, uri) = _layout[index];
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
        if( FindDefinedRepo( key, out var workingFolder, out var repo, out var index ) )
        {
            if( repo == null )
            {
                // We found the key in the layout (the url or a workingFolder): it logically
                // exists, we must try to fix the layout if we can't open the working folder.
                repo = DoLoadGitRepository( monitor, workingFolder, index, mustExist: true );
            }
        }
        else if( mustExist )
        {
            monitor.Error( $"The world '{_name}' doesn't contain the repository '{key}'." );
        }
        return repo;
    }

    Repo? DoLoadGitRepository( IActivityMonitor monitor, NormalizedPath p, int index, bool mustExist )
    {
        Repo? repo = null;
        if( Directory.Exists( p ) )
        {
            var repository = GitRepository.Open( monitor, _stackRepository.SecretsStore, p, p.LastPart, _stackRepository.IsPublic );
            if( repository != null )
            {
                repo = CreateRepo( index, repository );
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
                        repo = CreateRepo( index, repository );
                    }
                }
            }
        }
        return repo;
    }

    Repo CreateRepo( int index, GitRepository repository )
    {
        Repo? repo = new Repo( this, repository, index, _firstRepo );
        _firstRepo = repo;
        _cachedRepositories[repository.WorkingFolder] = repo;
        _cachedRepositories[repository.OriginUrl.ToString()] = repo;
        return repo;
    }

    /// <inheritdoc cref="WorldName.ToString"/>
    public override string ToString() => _name.ToString();

}
