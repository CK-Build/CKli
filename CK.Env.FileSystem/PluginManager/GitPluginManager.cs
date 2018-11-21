using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using CK.Text;

namespace CK.Env
{
    /// <summary>
    /// Handles Git plugins that can be <see cref="IGitPlugin"/> on the Git repository
    /// or <see cref="IGitBranchPlugin"/> that are scoped to a specific branch with associated
    /// settings that are explicitly registered mere objects.
    /// A default branch must be provided: plugins registered in this default branch are used as fallbacks
    /// for unconfigured branch.
    /// </summary>
    public class GitPluginManager : IDisposable
    {
        readonly GitPluginRegistry _registry;
        readonly CommandRegister _commandRegister;
        readonly string _defaultBranchName;
        readonly PluginCollection<IGitPlugin> _plugins;
        readonly Branches _branches;

        class Branches : IGitBranchPluginCollection, IDisposable
        {
            readonly GitPluginManager _manager;
            readonly Dictionary<string, PluginCollection<IGitBranchPlugin>> _branchPlugins;

            public Branches( GitPluginManager manager )
            {
                _manager = manager;
                _branchPlugins = new Dictionary<string, PluginCollection<IGitBranchPlugin>>();
            }

            public IGitPluginCollection<IGitBranchPlugin> this[ string branchName ] => FindOrCreate( branchName );

            public bool EnsurePlugins( IActivityMonitor m, string branchName, string holderName ) => FindOrCreate( branchName, holderName, m ) != null;

            PluginCollection<IGitBranchPlugin> FindOrCreate( string branchName, string holderName = null, IActivityMonitor m = null )
            {
                if( String.IsNullOrWhiteSpace( branchName ) ) throw new ArgumentNullException( nameof( branchName ) );
                if( !_branchPlugins.TryGetValue( branchName, out var c ) )
                {
                    using( m?.OpenTrace( $"Initializing plugins for '{holderName}' branch '{branchName}'." ) )
                    {
                        try
                        {
                            _manager._plugins.EnsureFirstLoad();
                            c = new PluginCollection<IGitBranchPlugin>( _manager, branchName );
                            _branchPlugins.Add( branchName, c.EnsureFirstLoad() );
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

            public void Reload( string branchName )
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

        class PluginCollection<T> : IGitPluginCollection<T>, IDisposable, IServiceProvider where T : class
        {
            readonly Dictionary<Type, object> _mappings;
            readonly GitPluginManager _manager;
            int _pluginCount;

            public PluginCollection( GitPluginManager manager, string branchName )
            {
                _manager = manager;
                BranchName = branchName;
                ServiceContainer = branchName == null
                                    ? manager.ServiceContainer
                                    : new SimpleServiceContainer( manager._plugins );
                _mappings = new Dictionary<Type, object>();
            }

            public SimpleServiceContainer ServiceContainer { get; }

            public PluginCollection<T> EnsureFirstLoad()
            {
                if( _pluginCount == 0 ) Reload();
                return this;
            }

            public void Reload()
            {
                if( _pluginCount != 0 ) Reset();
                _pluginCount = _manager._registry.FillMappings(
                                                    _mappings,
                                                    ServiceContainer,
                                                    _manager._commandRegister,
                                                    BranchName,
                                                    BranchName != null ? _manager._defaultBranchName : null );
            }

            public void Dispose() => Reset();

            void Reset()
            {
                var pluginKeys = _mappings.Keys.Where( k => GitPluginRegistry.IsGitFolderPlugin( k ) ).ToList();
                foreach( var k in pluginKeys )
                {
                    var p = _mappings[k];
                    if( p is ICommandMethodsProvider commandProvider)
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

            public string BranchName { get; }

            public int Count => _pluginCount;

            public IEnumerator<T> GetEnumerator() => _mappings.Values.OfType<T>().GetEnumerator();

            public T GetPlugin( Type t ) => _mappings.GetValueWithDefault( t, null ) as T;

            public P GetPlugin<P>() where P : T => (P)_mappings.GetValueWithDefault( typeof( P ), null );

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            object IServiceProvider.GetService( Type serviceType )
            {
                return _mappings.TryGetValue( serviceType, out var o ) ? o : ServiceContainer.GetService( serviceType );
            }
        }

        /// <summary>
        /// Initializes a new plugin manager.
        /// </summary>
        /// <param name="baseProvider">The base service provider.</param>
        /// <param name="commandRegister">Command registerer.</param>
        /// <param name="defaultBranchName">The default branch name (typically "develop"). Must not be null or empty.</param>
        /// <param name="branchesPath">Required root /branches path relative from the root FileSystem.</param>
        public GitPluginManager( ISimpleServiceContainer baseProvider, CommandRegister commandRegister, string defaultBranchName, NormalizedPath branchesPath )
        {
            if( String.IsNullOrWhiteSpace(defaultBranchName) ) throw new ArgumentNullException( nameof( defaultBranchName ) );
            if( commandRegister == null ) throw new ArgumentNullException( nameof( commandRegister ) );
            ServiceContainer = new SimpleServiceContainer( baseProvider );
            _defaultBranchName = defaultBranchName;
            _commandRegister = commandRegister;
            _registry = new GitPluginRegistry( branchesPath );
            _plugins = new PluginCollection<IGitPlugin>( this, null );
            _branches = new Branches( this );
        }

        /// <summary>
        /// Gets the primary service container.
        /// </summary>
        public SimpleServiceContainer ServiceContainer { get; }

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
        /// Exisitng IDisposable plugins are disposed first.
        /// </summary>
        /// <param name="branchName">The branch name or null for all plugins.</param>
        public void Reload( string branchName = null )
        {
            if( branchName == null ) _plugins.Reload();
            _branches.Reload( branchName );
        }

        /// <summary>
        /// Registers a type that must be <see cref="IGitPlugin"/>.
        /// The plugin will be available in all branches (after a subsequent <see cref="Reload"/>).
        /// </summary>
        /// <param name="pluginType">The type of the plugin. Must be a non null <see cref="IGitPlugin"/>.</param>
        public void Register( Type pluginType ) => _registry.Register( pluginType );

        /// <summary>
        /// Registers a type that must be <see cref="IGitBranchPlugin"/> (or a <see cref="IGitPlugin"/> if allowed).
        /// A branch plugin will be available only in the specific branch (after a subsequent <see cref="Reload"/>).
        /// </summary>
        /// <param name="pluginType">The type of the plugin. Must be a non null <see cref="IGitBranchPlugin"/> or <see cref="IGitPlugin"/> (if <paramref name="allowGitPlugin"/> is true).</param>
        /// <param name="branchName">Branch name. Must not be null.</param>
        /// <param name="allowGitPlugin">True to allow <see cref="IGitPlugin"/> to be registered.</param>
        public void Register( Type pluginType, string branchName, bool allowGitPlugin = false ) => _registry.Register( pluginType, branchName, allowGitPlugin );

        /// <summary>
        /// Registers a settings object.
        /// The instance will be available in all branches if <paramref name="branchName"/> is null.
        /// Note that if an instance has already been registered, it is replaced.
        /// It will be available after a subsequent <see cref="Reload"/>.
        /// </summary>
        /// <typeparam name="T">Type of the settings.</typeparam>
        /// <param name="instance">The instance. Must not be null.</param>
        /// <param name="branchName">The branch name or null for a root setting.</param>
        public void RegisterSettings<T>( T instance, string branchName = null ) => _registry.RegisterSettings( typeof(T), instance, branchName );

        /// <summary>
        /// Registers a settings object.
        /// The instance will be available in all branches if <paramref name="branchName"/> is null.
        /// Note that if an instance has already been registered, it is replaced.
        /// It will be available after a subsequent <see cref="Reload"/>.
        /// </summary>
        /// <param name="type">The type to register. Must not be null.</param>
        /// <param name="instance">The instance. Must not be null.</param>
        /// <param name="branchName">The branch name or null for a root setting.</param>
        public void RegisterSettings( Type type, object instance, string branchName = null ) => _registry.RegisterSettings( type, instance, branchName );

        void IDisposable.Dispose()
        {
            _branches.Dispose();
            _plugins.Dispose();
        }

    }
}
