using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.Core;

/// <summary>
/// The World handles the <see cref="Repo"/> that are defined in its <see cref="Layout"/>.
/// <para>
/// The only way to obtain a World is to use the StackRepository. A World has a "short" life time (just like
/// its <see cref="StackRepository"/>): it is bound to a command execution (even in interactive mode).
/// </para>
/// </summary>
public sealed partial class World
{
    /// <summary>
    /// Gets the <see cref="InformationalVersion"/> of this assembly.
    /// </summary>
    public static readonly InformationalVersion CKliVersion = InformationalVersion.ReadFromAssembly( typeof( PluginMachinery ).Assembly );

    /// <summary>
    /// Loads the core "CKli.Plugins.dll" and all its plugins.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="dllPath">The core "CKli.Plugins.dll" path.</param>
    /// <param name="context">The context (provides plugin configurations.)</param>
    /// <param name="recoverableError">
    /// Outputs whether the error may be fixed by deleting the CKli.CompiledPlugins.cs generated file if it exists
    /// and recompiling the CKli.Plugins project.
    /// </param>
    /// <param name="loader">
    /// Outputs a weak reference on the PluginLoadContext that may have been instantiated (and disposed) even if
    /// this fails and returns null: <see cref="WeakReference.IsAlive"/> must be false before trying to reload or
    /// recompile plugins.
    /// </param>
    /// <returns>The plugin factory on success, null on error.</returns>
    public delegate IPluginFactory? PluginLoaderFunction( IActivityMonitor monitor,
                                                          NormalizedPath dllPath,
                                                          PluginCollectorContext context,
                                                          out bool recoverableError,
                                                          out WeakReference? loader );

    static PluginLoaderFunction? _pluginLoader;
    static Func<IPluginFactory>? _directPluginFactory;

    /// <summary>
    /// Gets or sets once the loader for plugins.
    /// <para>
    /// When not set, plugins are disabled. Once set, it cannot be changed.
    /// </para>
    /// </summary>
    public static PluginLoaderFunction? PluginLoader
    {
        get => _pluginLoader;
        set
        {
            Throw.CheckState( "Once set, PluginLoader cannot be changed.", _pluginLoader == null || value != _pluginLoader );
            _pluginLoader = value;
        }
    }

    /// <summary>
    /// Gets or sets a factory for <see cref="IPluginFactory"/> that will be used instead of the regular <see cref="PluginLoader"/>.
    /// <para>
    /// This can always be set (not only once like the <see cref="PluginLoader"/>).
    /// </para>
    /// </summary>
    public static Func<IPluginFactory>? DirectPluginFactory
    {
        get => _directPluginFactory;
        set => _directPluginFactory = value;
    }

    readonly StackRepository _stackRepository;
    readonly ScreenType _screenType;
    readonly LocalWorldName _name;
    readonly WorldDefinitionFile _definitionFile;
    readonly WorldEvents _events;
    readonly PluginMachinery? _pluginMachinery;
    PluginCollection? _plugins;
    Command? _executingCommand;

    // The WorldDefinitionFile maintains its layout list.
    // AddRepository, RemoveRepository and XifLayout are the only ones that can
    // update this layout.
    readonly IReadOnlyList<RepoLayout> _layout;

    // This caches the Repo (and its GitRepository) by uri and path as string and case insensitively.
    // This enables to cache the Repo even with case mismatch (that FixLayout will fix).
    readonly Dictionary<string, Repo?> _cachedRepositories;
    readonly Dictionary<RandomId, Repo> _ckliRepoIndex;
    Repo[]? _allRepositories;
    Repo? _firstRepo;

    World( StackRepository stackRepository,
           ScreenType screenType,
           LocalWorldName name,
           WorldDefinitionFile definitionFile,
           IReadOnlyList<RepoLayout> layout,
           PluginMachinery? pluginMachinery )
    {
        _stackRepository = stackRepository;
        _screenType = screenType;
        _name = name;
        _definitionFile = definitionFile;
        _layout = layout;
        _pluginMachinery = pluginMachinery;
        _cachedRepositories = new Dictionary<string, Repo?>( layout.Count, StringComparer.OrdinalIgnoreCase );
        _ckliRepoIndex = new Dictionary<RandomId, Repo>( layout.Count );
        FillCachedRepositories();
        _events = new WorldEvents();
    }

    void FillCachedRepositories()
    {
        Throw.DebugAssert( _cachedRepositories.Count == 0 );
        foreach( var (url, _, path) in _layout )
        {
            _cachedRepositories.Add( path, null );
            _cachedRepositories.Add( url.ToString(), null );
        }
    }

    /// <summary>
    /// Only called by the StackRepository.
    /// Fails if the world layout cannot be loaded or plugins initialization fails (null is returned).
    /// </summary>
    internal static World? Create( IActivityMonitor monitor,
                                   ScreenType screenType,
                                   StackRepository stackRepository,
                                   NormalizedPath path,
                                   bool withPlugins )
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
        if( withPlugins && _directPluginFactory == null )
        {
            if( _pluginLoader != null )
            {
                machinery = new PluginMachinery( worldName, definitionFile );
                machinery.Initialize( monitor );
            }
            else
            {
                monitor.Info( ScreenType.CKliScreenTag, "Plugins are disabled because there is no configured World.PluginLoader." );
            }
        }
        var w = new World( stackRepository, screenType, worldName, definitionFile, layout, machinery );
        // AcquirePlugins returns false on exception or when a plugin Initialize() method returns false.
        // It returns true if the load fails and a NoPluginFactory is used to create an empty plugin
        // collection: in this case we want a World to be able to honor plugin add/remove plugin commands.
        if( withPlugins
            && (machinery != null || _directPluginFactory != null)
            && !w.AcquirePlugins( monitor ) )
        {
            w = null;
        }
        return w;
    }

    internal bool AcquirePlugins( IActivityMonitor monitor )
    {
        // This is called from OnPluginChanged only when _pluginMachinery != null.
        // Otherwise this is called from Create and only when withPlugin is true and at least _directPluginFactory or _pluginMachinery exist.
        Throw.DebugAssert( "Never called when created without plugins.", _directPluginFactory != null || _pluginMachinery != null );
        try
        {
            IPluginFactory? f = _directPluginFactory?.Invoke();
            if( f == null )
            {
                Throw.DebugAssert( _pluginMachinery != null );
                f = _pluginMachinery.PluginFactory;
            }
            _plugins = f.Create( monitor, this );
            return _plugins.CallPluginsInitialization( monitor );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While instantiating plugins.", ex );
            return false;
        }
    }

    internal void ReleasePlugins()
    {
        _events.ReleaseEvents();
        if( _plugins != null )
        {
            _plugins.Commands.Clear();
            _plugins.DisposeDisposablePlugins();
            _plugins = null;
        }
        _pluginMachinery?.ReleasePluginFactory();
    }

    internal void DisposeRepositoriesAndReleasePlugins()
    {
        ReleasePlugins();
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
    /// Gets the screen type.
    /// </summary>
    public ScreenType ScreenType => _screenType;

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
    public IReadOnlyList<RepoLayout> Layout => _layout;

    /// <summary>
    /// Gets all events that can be raised by a World.
    /// </summary>
    public WorldEvents Events => _events;

    /// <summary>
    /// Finds a Repo by its <see cref="Repo.CKliRepoId"/>.
    /// <para>
    /// Setting <paramref name="alreadyLoadedOnly"/> to true is an optimization.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="id">The repository identifier.</param>
    /// <param name="alreadyLoadedOnly">True to not load all defined Repo and only consider the already loaded ones.</param>
    /// <returns>The Repo or null.</returns>
    public Repo? FindByCKliRepoId( IActivityMonitor monitor, RandomId id, bool alreadyLoadedOnly = false )
    {
        if( !alreadyLoadedOnly
            && !IsAllRepoLoaded
            && GetAllDefinedRepo( monitor ) == null )
        {
            return null;
        }
        return _ckliRepoIndex.GetValueOrDefault( id );
    }

    /// <summary>
    /// Gets whether a full path or a origin url is defined in this <see cref="Layout"/>.
    /// The lookup is case insensitive.
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
    /// Gets all <see cref="Repo"/> from the provided path (case insensitive) that must be below or equal
    /// to this <see cref="LocalWorldName.WorldRoot"/> (otherwise an <see cref="ArgumentException"/> is thrown).
    /// <para>
    /// The returned list may be empty if this world is empty.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="path">The path that must be below the world root.</param>
    /// <returns>The repositories or null on error.</returns>
    public IReadOnlyList<Repo>? GetAllDefinedRepo( IActivityMonitor monitor, NormalizedPath path )
    {
        Throw.CheckArgument( IsBelowPathOrEqual( _name.WorldRoot, path ) );
        var worldPath = _name.WorldRoot.Path;
        if( _name.WorldRoot.Path.Length == path.Path.Length )
        {
            return GetAllDefinedRepo( monitor );
        }

        // Load only one Repo if possible.
        var belowOne = TryLoadDefinedGitRepository( monitor, path, false );
        if( belowOne != null ) return [belowOne];

        var all = GetAllDefinedRepo( monitor );
        if( all == null ) return null;
        var result = new List<Repo>();
        foreach( var repo in all )
        {
            var p = repo.WorkingFolder.Path;
            Throw.DebugAssert( "We must have resolved it above (belowOne).",
                               !p.StartsWith( path, StringComparison.OrdinalIgnoreCase ) || p.Length > path.Path.Length );
            if( p.StartsWith( path, StringComparison.OrdinalIgnoreCase )
                && p[path.Path.Length] == '/' )
            {
                result.Add( repo );
            } 
        }
        return result;
    }

    /// <summary>
    /// Gets whether all the <see cref="Repo"/> are loaded: <see cref="GetAllDefinedRepo(IActivityMonitor)"/>
    /// has been called at least once.
    /// </summary>
    public bool IsAllRepoLoaded => _allRepositories != null;

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
            var (url, _, path) = _layout[index];
            if( IsBelowPathOrEqual( path, key )
                || url.ToString().Equals( key, StringComparison.OrdinalIgnoreCase ) )
            {
                return path;
            }
        }
        return default;
    }

    static bool IsBelowPathOrEqual( NormalizedPath path, string candidate )
    {
        var p = path.Path;
        return candidate.StartsWith( p, StringComparison.OrdinalIgnoreCase )
                && (p.Length == candidate.Length || candidate[p.Length] is '/' or '\\');
    }

    Repo? TryLoadDefinedGitRepository( IActivityMonitor monitor, string key, bool mustExist )
    {
        if( FindDefinedRepo( key, out var workingFolder, out var repo, out var index ) )
        {
            // We found the key in the layout (the url or a workingFolder): it logically
            // exists, we must try to fix the layout if we can't open the working folder.
            repo ??= DoLoadGitRepository( monitor, workingFolder, index, mustExist: true );
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
            var repository = GitRepository.Open( monitor,
                                                 _stackRepository.SecretsStore,
                                                 _stackRepository.Context.Committer,
                                                 p,
                                                 p.RemoveFirstPart( _name.WorldRoot.Parts.Count ),
                                                 _stackRepository.IsPublic,
                                                 _layout[index].Url );
            if( repository != null )
            {
                repo = CreateRepo( monitor, index, repository );
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
                    var repository = GitRepository.Open( monitor,
                                                         _stackRepository.SecretsStore,
                                                         _stackRepository.Context.Committer,
                                                         p,
                                                         p.RemoveFirstPart( _name.WorldRoot.Parts.Count ),
                                                         _stackRepository.IsPublic,
                                                         _layout[index].Url );
                    if( repository != null )
                    {
                        repo = CreateRepo( monitor, index, repository );
                    }
                }
            }
        }
        return repo;
    }

    Repo CreateRepo( IActivityMonitor monitor, int index, GitRepository repository )
    {
        if( !TryReadCKliRepoTag( monitor, repository, _stackRepository, out var repoId ) )
        {
            if( !repoId.IsValid )
            {
                repoId = RandomId.CreateRandom();
            }
            CreateOrUpdateCKliRepoTag( repository, _stackRepository, repoId );
            repository.DeferredPushRefSpecs.Add( "+refs/tags/ckli-repo" );
        }
        Repo? repo = new Repo( this, repository, _layout[index].XElement, index, repoId, _firstRepo );
        _firstRepo = repo;
        _cachedRepositories[repository.WorkingFolder] = repo;
        _cachedRepositories[repository.RepositoryKey.OriginUrl.ToString()] = repo;
        _ckliRepoIndex.Add( repoId, repo );
        return repo;

        static bool TryReadCKliRepoTag( IActivityMonitor monitor,
                                        GitRepository git,
                                        StackRepository stackRepository,
                                        out RandomId repoId )
        {
            repoId = default;
            var message = git.Repository.Tags["ckli-repo"]?.Annotation?.Message;
            if( message == null )
            {
                monitor.Info( $"No 'ckli-repo' tag found for '{git.DisplayPath}'. A new 'ckli-repo' tag will be created." );
                return false;
            }
            var s = message.AsSpan();
            bool sameStack = false;
            bool repoIdTrulyParsed = false;
            if( s.TryMatch( "Id: " )
                && RandomId.TryMatch( ref s, out repoId )
                && repoId.IsValid
                && (repoIdTrulyParsed = s.TrySkipWhiteSpaces( 1 ))
                && s.TryMatch( "Stack: " )
                && (sameStack = s.TryMatch( stackRepository.OriginUrl.ToString() ))
                && s.SkipWhiteSpaces()
                && s.Length == 0 )
            {
                return true;
            }
            if( !repoIdTrulyParsed ) repoId = default;

            string msg = repoId.IsValid
                        ? "Found a valid CKliRepoId."
                        : "Unable to parse Id. A new CKliRepoId will be generated.";
            if( !sameStack ) msg += $"""

                    Stack has changed. It will be updated to '{stackRepository.OriginUrl}'.
                    """;
            monitor.Log( repoId.IsValid ? LogLevel.Info : LogLevel.Warn, $"""
                The 'ckli-repo' tag in '{git.DisplayPath}' is:
                {message}

                {msg}
                The tag will be updated.
                """ );
            return false;

        }

        static void CreateOrUpdateCKliRepoTag( GitRepository repo, StackRepository stackRepository, RandomId repoId )
        {
            var r = repo.Repository;
            r.Tags.Add( "ckli-repo", r.Head.Tip, repo.Committer, $"""
                Id: {repoId}
                Stack: {stackRepository.OriginUrl}

                """,
                allowOverwrite: true );
        }
    }

    /// <inheritdoc cref="WorldName.ToString"/>
    public override string ToString() => _name.ToString();

}
