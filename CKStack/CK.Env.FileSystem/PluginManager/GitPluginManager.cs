using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CK.Env
{
    /// <summary>
    /// Handles Git plugins that can be <see cref="IGitPlugin"/> on the Git repository
    /// or <see cref="IGitBranchPlugin"/> that are scoped to a specific branch with associated
    /// settings that are explicitly registered mere objects.
    /// A default branch must be provided: plugins registered in this default branch are used as fallbacks
    /// for unconfigured branch.
    /// </summary>
    public sealed class GitPluginManager : IDisposable
    {
        readonly CommandRegistry _commandRegister;
        readonly string _defaultBranchName;
        readonly PluginCollection<IGitPlugin> _plugins;
        readonly BranchesPluginCollection _branches;

        sealed class BranchesPluginCollection : IGitBranchPluginCollection, IDisposable
        {
            readonly GitPluginManager _manager;
            readonly Dictionary<string, PluginCollection<IGitBranchPlugin>> _branchPlugins;

            public BranchesPluginCollection( GitPluginManager manager )
            {
                _manager = manager;
                _branchPlugins = new Dictionary<string, PluginCollection<IGitBranchPlugin>>();
            }

            public bool IsInitialized( string branchName )
            {
                Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
                return _branchPlugins.TryGetValue( branchName, out var c ) && c.IsFirstLoadDone;
            }

            internal PluginCollection<IGitBranchPlugin> FindOrCreateWithoutInitialization( string branchName ) => DoFindOrCreate( false, branchName )!;

            public IGitPluginCollection<IGitBranchPlugin> this[string branchName] => DoFindOrCreate( true, branchName )!;

            public bool EnsurePlugins( IActivityMonitor m, string branchName ) => DoFindOrCreate( true, branchName, m ) != null;

            PluginCollection<IGitBranchPlugin>? DoFindOrCreate( bool ensureFirstLoad, string branchName, IActivityMonitor? m = null )
            {
                Throw.CheckNotNullOrWhiteSpaceArgument( branchName );
                if( !_branchPlugins.TryGetValue( branchName, out var c )
                    || (ensureFirstLoad && !c.IsFirstLoadDone) )
                {
                    using( ensureFirstLoad && m != null ? m.OpenTrace( $"Initializing plugins for '{_manager.Registry.FolderPath}' branch '{branchName}'." ) : null )
                    {
                        try
                        {
                            _manager._plugins.EnsureFirstLoad();
                            if( c == null )
                            {
                                c = new PluginCollection<IGitBranchPlugin>( _manager, branchName );
                                _branchPlugins.Add( branchName, ensureFirstLoad ? c.EnsureFirstLoad() : c );
                            }
                            else
                            {
                                c.EnsureFirstLoad();
                            }
                        }
                        catch( Exception ex )
                        {
                            if( m == null ) throw;
                            m.Error( ex );
                            return null;
                        }
                    }
                }
                return c;
            }

            public int Count => _branchPlugins.Count;

            public IEnumerator<IGitPluginCollection<IGitBranchPlugin>> GetEnumerator() => _branchPlugins.Values.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            /// <summary>
            /// Reloads plugins from a branch or, when <paramref name="branchName"/> is null, all
            /// plugins (<see cref="IGitPlugin"/> and <see cref="IGitBranchPlugin"/> for all branches).
            /// Note that plugins that supports <see cref="IDisposable"/> are disposed.
            /// </summary>
            /// <param name="branchName">Branch name to reload. Null to reload all plugins.</param>
            public void Reload( string? branchName )
            {
                if( branchName == null )
                {
                    foreach( var c in _branchPlugins.Values ) c.Reload();
                }
                else
                {
                    if( _branchPlugins.TryGetValue( branchName, out var b ) ) b.Reload();
                }
            }

            public void Dispose()
            {
                foreach( var b in _branchPlugins.Values )
                {
                    b.Dispose();
                }
                _branchPlugins.Clear();
            }
        }

        sealed class PluginCollection<T> : IGitPluginCollection<T>, IDisposable, IServiceProvider where T : class
        {
            readonly Dictionary<Type, object> _mappings;
            readonly GitPluginManager _manager;
            readonly SimpleServiceContainer _serviceContainer;
            int _pluginCount;

            public PluginCollection( GitPluginManager manager, string? branchName )
            {
                _manager = manager;
                BranchName = branchName;
                if( branchName == null )
                {
                    _serviceContainer = manager.ServiceContainer;
                }
                else
                {
                    if( branchName == manager._defaultBranchName )
                    {
                        _serviceContainer = new SimpleServiceContainer( manager._plugins );
                    }
                    else
                    {
                        var baseProvider = manager._branches.FindOrCreateWithoutInitialization( manager._defaultBranchName );
                        _serviceContainer = new SimpleServiceContainer( baseProvider );
                    }
                }
                _mappings = new Dictionary<Type, object>();
            }

            public SimpleServiceContainer ServiceContainer => _serviceContainer;

            /// <summary>
            /// Gets whether plugins have been initialized at least one: consider that as long as no plugins
            /// are loaded, this is false.
            /// </summary>
            public bool IsFirstLoadDone => _pluginCount > 0;

            public PluginCollection<T> EnsureFirstLoad()
            {
                if( _pluginCount == 0 ) Reload();
                return this;
            }

            public void Reload()
            {
                if( _pluginCount != 0 ) Reset();
                // Collects the settings.
                var updatedMappings = new Dictionary<Type, object>( _mappings );
                _pluginCount = _manager.Registry.FillMappings( updatedMappings,
                                                               _serviceContainer,
                                                               _manager._commandRegister,
                                                               BranchName,
                                                               BranchName != null ? _manager._defaultBranchName : null );
                _mappings.Clear();
                _mappings.AddRange( updatedMappings );
            }

            public void Dispose() => Reset();

            void Reset()
            {
                var pluginKeys = _mappings.Keys.Where( k => GitPluginRegistry.IsGitFolderPlugin( k ) ).ToList();
                foreach( var k in pluginKeys )
                {
                    var p = _mappings[k];
                    if( p is ICommandMethodsProvider commandProvider )
                    {
                        _manager._commandRegister.Unregister( commandProvider );
                    }
                    if( p is IDisposable disposable )
                    {
                        disposable.Dispose();
                    }
                    _mappings.Remove( k );
                }
            }

            public string? BranchName { get; }

            public int Count => _pluginCount;

            public IEnumerator<T> GetEnumerator() => _mappings.Values.OfType<T>().GetEnumerator();

            public T? GetPlugin( Type t ) => _mappings.GetValueOrDefault( t ) as T;

            public P? GetPlugin<P>() where P : T => (P?)_mappings.GetValueOrDefault( typeof( P ) );

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            object? IServiceProvider.GetService( Type serviceType )
            {
                return _mappings.TryGetValue( serviceType, out var o ) ? o : ServiceContainer.GetService( serviceType );
            }
        }

        /// <summary>
        /// Initializes a new plugin manager.
        /// </summary>
        /// <param name="registry">The registry of plugins.</param>
        /// <param name="baseProvider">The base service provider.</param>
        /// <param name="commandRegistry">Commands registry.</param>
        /// <param name="defaultBranchName">The default branch name (typically "develop"). Must not be null or empty.</param>
        public GitPluginManager( GitPluginRegistry registry, ISimpleServiceContainer baseProvider, CommandRegistry commandRegistry, string defaultBranchName )
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( defaultBranchName );
            ServiceContainer = new SimpleServiceContainer( baseProvider );
            _defaultBranchName = defaultBranchName;
            _commandRegister = commandRegistry ?? throw new ArgumentNullException( nameof( commandRegistry ) );
            Registry = registry;
            _plugins = new PluginCollection<IGitPlugin>( this, null );
            _branches = new BranchesPluginCollection( this );
        }

        /// <summary>
        /// Gets the plugin registry.
        /// Any registration of new <see cref="IGitPlugin"/> (or <see cref="IGitBranchPlugin"/>) must be followed
        /// by a <see cref="Reload"/> for actual plugins to be instantiated.
        /// </summary>
        public GitPluginRegistry Registry { get; }

        /// <summary>
        /// Gets the primary service container. Use <see cref="GetServiceContainer(string)"/> to obtain the
        /// service container for a branch.
        /// </summary>
        public SimpleServiceContainer ServiceContainer { get; }

        /// <summary>
        /// Gets a <see cref="SimpleServiceContainer"/> for a branch or, if <paramref name="branchName"/> is null,
        /// the primary <see cref="ServiceContainer"/> without triggering plugin initialization.
        /// </summary>
        /// <param name="branchName">Branch name.</param>
        /// <returns>The primary service container or the container for the branch.</returns>
        public SimpleServiceContainer GetServiceContainer( string? branchName )
        {
            if( branchName == null ) return ServiceContainer;
            return _branches.FindOrCreateWithoutInitialization( branchName ).ServiceContainer;
        }

        /// <summary>
        /// Gets the root <see cref="IGitPlugin"/> plugins.
        /// </summary>
        public IGitPluginCollection<IGitPlugin> Plugins => _plugins.EnsureFirstLoad();

        /// <summary>
        /// Gets the <see cref="IGitBranchPluginCollection"/> that exposes
        /// the <see cref="IGitBranchPlugin"/> plugins for each branch.
        /// </summary>
        public IGitBranchPluginCollection BranchPlugins => _branches;

        /// <summary>
        /// Reloads the whole set of plugins or the ones of a specific branch.
        /// Existing IDisposable plugins are disposed first.
        /// </summary>
        /// <param name="branchName">The branch name or null for all plugins.</param>
        public void Reload( string? branchName = null )
        {
            if( branchName == null ) _plugins.Reload();
            _branches.Reload( branchName );
        }

        void IDisposable.Dispose()
        {
            _branches.Dispose();
            _plugins.Dispose();
        }

    }
}
