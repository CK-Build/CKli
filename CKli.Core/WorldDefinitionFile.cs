using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.Core;

/// <summary>
/// Helper around a World.xml definition file.
/// </summary>
public sealed class WorldDefinitionFile
{
    static Func<IActivityMonitor,string,string>? _repositoryUrlHook;
    readonly XElement _root;
    readonly XElement _plugins;
    readonly LocalWorldName _world;
    List<(NormalizedPath, Uri)>? _layout;
    Dictionary<string, (XElement Config, bool IsDisabled)>? _pluginsConfiguration;
    bool _allowEdit;
    bool _isDirty;

    /// <summary>
    /// Drives the <see cref="Repo.Index"/>.
    /// </summary>
    public enum LayoutRepoOrder
    {
        /// <summary>
        /// Follows the order in the definition file.
        /// This is the default.
        /// </summary>
        DefinitionFile,

        /// <summary>
        /// Orders by the repository path (consider the layout folders).
        /// </summary>
        Path,

        /// <summary>
        /// Orders by repository name.
        /// </summary>
        Name
    }

    /// <summary>
    /// Gets or sets the <see cref="LayoutRepoOrder"/>.
    /// </summary>
    public static LayoutRepoOrder RepoOrder { get; set; }

    WorldDefinitionFile( LocalWorldName world, XElement root, XElement plugins )
    {
        Throw.DebugAssert( root.Document != null );
        _root = root;
        _plugins = plugins;
        _root.Document.Changing += OnDocumentChanging;
        _world = world;
    }

    void OnDocumentChanging( object? sender, XObjectChangeEventArgs e )
    {
        if( _allowEdit )
        {
            _isDirty = true;
        }
        else
        {
            Throw.InvalidOperationException( "Xml document must not be changed." );
        }
    }

    /// <summary>
    /// Gets the world defined by this file.
    /// </summary>
    public LocalWorldName World => _world;

    /// <summary>
    /// Gets the root element.
    /// Must not be mutated otherwise a <see cref="InvalidOperationException"/> is raised.
    /// </summary>
    public XElement XmlRoot => _root;

    /// <summary>
    /// Gets the &lt;Plugins /&gt; element.
    /// Must not be mutated otherwise a <see cref="InvalidOperationException"/> is raised.
    /// </summary>
    public XElement Plugins => _plugins;

    /// <summary>
    /// Gets whether &lt;Plugins Disabled="true" /&gt; is set.
    /// </summary>
    public bool IsPluginsDisabled => (bool?)_plugins.Attribute( _xDisabled ) is true;

    /// <summary>
    /// Reads the &lt;Plugins&gt; section.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The plugins configuration or null if errors have been detected.</returns>
    public IReadOnlyDictionary<string,(XElement Config, bool IsDisabled)>? ReadPluginsConfiguration( IActivityMonitor monitor )
    {
        return _pluginsConfiguration  ??= DoReadPluginsConfiguration( monitor );
    }

    /// <summary>
    /// Enables or disables a plugin or all of them if <paramref name="name"/> is "global".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="name">The plugin to enable or "global".</param>
    /// <param name="enable">True to enable, false to disable.</param>
    /// <returns>True on success, false on error.</returns>
    public bool EnablePlugin( IActivityMonitor monitor, string name, bool enable )
    {
        var config = ReadPluginsConfiguration( monitor );
        if( config == null ) return false;
        bool isGlobal = name == "global";
        XElement? e = null;
        if( !isGlobal )
        {
            if( !PluginMachinery.EnsureFullPluginName( monitor, name, out var shortPluginName, out var longPluginName ) )
            {
                return false;
            }
            e = config.GetValueOrDefault( name ).Config;
            if( e == null )
            {
                monitor.Error( $"Unable to find Plugin configuration '{shortPluginName}'." );
                return false;
            }
            if( (bool?)_plugins.Attribute( _xDisabled ) is true == !enable )
            {
                return true;
            }
        }
        else
        {
            e = _plugins;
            if( IsPluginsDisabled == !enable )
            {
                return true;
            }
        }
        using( StartEdit() )
        {
            e.SetAttributeValue( _xDisabled, enable ? null : "true" );
        }
        return SaveFile( monitor );
    }

    Dictionary<string, (XElement Config, bool IsDisabled)>? DoReadPluginsConfiguration( IActivityMonitor monitor )
    {
        bool success = true;
        var config = new Dictionary<string, (XElement Config, bool IsDisabled)>( StringComparer.OrdinalIgnoreCase );
        foreach( var e in _plugins.Elements() )
        {
            var name = e.Name.LocalName;
            if( config.TryGetValue( name, out var exists ) )
            {
                monitor.Error( $"""
                        Duplicate Plugin configuration found:
                        {exists.Config}
                        and:
                        {e}
                        """ );
                success = false;
            }
            else
            {
                config.Add( name, (e, (bool?)e.Attribute( _xDisabled ) is true) );
            }
        }
        return success ? config : null;
    }

    /// <summary>
    /// Shallow and quick analysis of the &lt;Folder&gt; and &lt;Repository&gt; elements that
    /// checks the unicity of "Path" and "Url" attribute.
    /// The list is ordered by the Path (that are absolute).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The layout of the repositories or null if errors have been detected.</returns>
    public IReadOnlyList<(NormalizedPath Path, Uri Uri)>? ReadLayout( IActivityMonitor monitor )
    {
        return _layout ??= GetRepositoryLayout( monitor, _root, _world );
    }

    /// <summary>
    /// Gets or sets an optional transformer of the &lt;Repository Url="..." /&gt; value.
    /// <para>
    /// This is mainly for tests. Note that this is applied when loading the xml file definition:
    /// the in-memory representation is no more the same as the file. Saving the definition
    /// file is required for the file to be updated with the transformations.
    /// </para>
    /// </summary>
    public static Func<IActivityMonitor, string, string>? RepositoryUrlHook
    {
        get => _repositoryUrlHook;
        set => _repositoryUrlHook = value;
    }

    /// <summary>
    /// Checks whether a &lt;Folder Name="..." /&gt; is valid.
    /// </summary>
    /// <param name="name">Folder name.</param>
    /// <returns>Name validity.</returns>
    public static bool IsValidFolderName( string? name )
    {
        var sName = name.AsSpan().Trim();
        return sName.Length > 0 && FileUtil.IndexOfInvalidFileNameChars( sName ) < 0;
    }

    internal IDisposable StartEdit()
    {
        Throw.DebugAssert( !_allowEdit );
        _allowEdit = true;
        return Util.CreateDisposableAction( () => _allowEdit = false );
    }

    internal void AddRepository( IActivityMonitor monitor, NormalizedPath path, IEnumerable<string> folders, Uri uri )
    {
        Throw.DebugAssert( folders.All( IsValidFolderName ) );
        Throw.DebugAssert( GitRepositoryKey.CheckAndNormalizeRepositoryUrl( uri ) == uri );
        Throw.DebugAssert( _allowEdit );

        // Normalizing "Repository Proxy" url.
        string urlValue = NormalizeRepositoryProxyUrl( monitor, uri );
        XElement folder = EnsureFolder( folders, _root );
        folder.Add( new XElement( _xRepository, new XAttribute( _xUrl, urlValue ) ) );
        static XElement EnsureFolder( IEnumerable<string> folders, XElement root )
        {
            XElement existing = root;
            bool found = true;
            var e = folders.GetEnumerator();
            while( e.MoveNext() )
            {
                var f = existing.Elements( _xFolder )
                                .FirstOrDefault( f => f.Attributes()
                                                       .Any( a => a.Name == _xName
                                                                  && a.Value.Equals( e.Current, StringComparison.OrdinalIgnoreCase ) ) );
                if( f == null )
                {
                    found = false;
                    break;
                }
                existing = f;
            }
            if( !found )
            {
                do
                {
                    var newOne = new XElement( _xFolder, new XAttribute( _xName, e.Current ) );
                    var nextName = existing.Elements( _xFolder ).FirstOrDefault( x => x.Attribute( _xName )!.Value.CompareTo( e.Current ) > 0 );
                    if( nextName != null )
                    {
                        nextName.AddBeforeSelf( newOne );
                    }
                    else
                    {
                        existing.Add( newOne );
                    }
                    existing = newOne;
                }
                while( e.MoveNext() );
            }
            return existing;
        }
    }

    internal bool RemoveRepository( IActivityMonitor monitor, Uri uri, bool removeEmptyFolder )
    {
        Throw.DebugAssert( _allowEdit );
        Throw.DebugAssert( _layout != null );

        string urlValue = NormalizeRepositoryProxyUrl( monitor, uri );
        var node = _root.Descendants( _xRepository )
                        .FirstOrDefault( e => e.Attribute( _xUrl )?.Value == urlValue );
        if( node == null )
        {
            monitor.Error( $"""
                Unable to find <Repository Url="{uri}" /> in '{_world.FullName}' definition file:
                {_root}
                """ );
            return false;
        }
        var parent = node.Parent;
        Throw.DebugAssert( parent != null );
        node.Remove();
        if( removeEmptyFolder )
        {
            while( parent != _root && !parent.HasElements )
            {
                var toRemove = parent;
                parent = parent.Parent;
                Throw.DebugAssert( parent != null );
                toRemove.Remove();
            }
        }
        return true;
    }

    string NormalizeRepositoryProxyUrl( IActivityMonitor monitor, Uri uri )
    {
        string? urlValue = null;
        if( uri.IsFile
            && !_world.Stack.LocalProxyRepositoriesPath.IsEmptyPath )
        {
            var local = new NormalizedPath( uri.LocalPath );
            if( local.StartsWith( _world.Stack.LocalProxyRepositoriesPath ) )
            {
                urlValue = local.LastPart;
                monitor.Trace( $"Automatic Repository Proxy rewrite for '{uri}'." );
            }
        }
        urlValue ??= uri.ToString();
        return urlValue;
    }

    internal void RemoveEmptyFolders()
    {
        Throw.DebugAssert( _allowEdit );
        _root.Descendants( _xFolder ).Where( e => !e.HasElements ).Remove();
    }

    internal bool SaveFile( IActivityMonitor monitor )
    {
        Throw.DebugAssert( !_allowEdit );
        if( _isDirty )
        {
            var path = _world.XmlDescriptionFilePath;
            try
            {
                _root.Document!.SaveWithoutXmlDeclaration( path );
                _isDirty = false;
                monitor.Trace( $"File '{path.LastPart}' saved." );
                if( _layout != null )
                {
                    // Reloading the layout to honor sort and also to double check
                    // that everything is fine.
                    // We keep the container and change its content.
                    var newLayout = GetRepositoryLayout( monitor, _root, _world );
                    if( newLayout == null ) return false;
                    _layout.Clear();
                    _layout.AddRange( newLayout );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While saving '{path}'.", ex );
                return false;
            }
        }
        return true;
    }

    static readonly XName _xDisabled = XNamespace.None + "Disabled";
    static readonly XName _xPlugins = XNamespace.None + "Plugins";
    static readonly XName _xRepository = XNamespace.None + "Repository";
    static readonly XName _xFolder = XNamespace.None + "Folder";
    static readonly XName _xName = XNamespace.None + "Name";
    static readonly XName _xUrl = XNamespace.None + "Url";

    internal static WorldDefinitionFile Create( IActivityMonitor monitor, LocalWorldName world, XElement root )
    {
        var plugins = root.Element( _xPlugins );
        if( plugins == null )
        {
            plugins = new XElement( _xPlugins );
            root.AddFirst( plugins );
        }
        if( _repositoryUrlHook != null )
        {
            foreach( var a in root.Descendants( _xRepository ).Attributes( _xUrl ) )
            {
                if( a.Value != null ) a.Value = _repositoryUrlHook( monitor, a.Value );
            }
        }
        return new WorldDefinitionFile( world, root, plugins );
    }

    static List<(NormalizedPath Path, Uri Uri)>? GetRepositoryLayout( IActivityMonitor monitor,
                                                                      XElement root,
                                                                      LocalWorldName world )
    {
        var list = new List<(NormalizedPath Path, Uri Uri)>();
        bool hasError = false;

        NormalizedPath worldRoot = world.WorldRoot;
        Process( monitor, root, world, worldRoot, list, ref hasError );
        if( hasError ) return null;
        var uniqueCheck = new Dictionary<string,Uri>();
        var uniquePath = new HashSet<NormalizedPath>();
        var uniqueUrl = new HashSet<Uri>();
        foreach( var (path, url) in list )
        {
            // These 2 checks guaranties that path <-> url is unique
            // and that repo
            if( !uniqueCheck.TryAdd( path, url ) )
            {
                if( url == uniqueCheck[path] )
                {
                    monitor.Error( $"Duplicate found: the repository '{path}' -> '{url}' definition must occur only once." );
                }
                else
                {
                    monitor.Error( $"Path '{path}' is associated to both '{url}' and '{uniqueCheck[path]}'." );
                }
                hasError = true;
            }
            if( !uniqueUrl.Add( url ) )
            {
                monitor.Error( $"Repository with Url '{url}' occurs more than once." );
                hasError = true;
            }
            else
            {
                if( path.Parts.Count > 1 )
                {
                    var repoName = $"repository name '{path.LastPart}'";
                    if( !uniqueCheck.TryAdd( repoName, url ) )
                    {
                        monitor.Error( $"Repository url '{url}' and '{uniqueCheck[path]}' have the same '{repoName}'." );
                        hasError = true;
                    }
                }
            }
        }
        if( hasError ) return null;
        if( RepoOrder == LayoutRepoOrder.Path )
        {
            list.Sort( ( e1, e2 ) => e1.Path.Path.AsSpan( worldRoot.Path.Length ).CompareTo( e2.Path.Path.AsSpan( worldRoot.Path.Length ), StringComparison.OrdinalIgnoreCase ) );
        }
        else if( RepoOrder == LayoutRepoOrder.Name )
        {
            list.Sort( ( e1, e2 ) => StringComparer.OrdinalIgnoreCase.Compare( e1.Path.LastPart, e2.Path.LastPart ) );
        }
        return list;

        static void Process( IActivityMonitor monitor,
                             XElement e,
                             LocalWorldName world,
                             in NormalizedPath p,
                             List<(NormalizedPath, Uri)> list,
                             ref bool hasError )
        {
            foreach( var c in e.Elements() )
            {
                var eN = c.Name.LocalName;
                if( eN == _xFolder.LocalName )
                {
                    string? name = c.Attribute( _xName )?.Value;
                    if( !IsValidFolderName( name ) )
                    {
                        monitor.Error( $"""
                                        Invalid element:
                                        {c}
                                        Attribute Name="..." is missing or invalid.
                                        """ );
                        hasError = true;
                    }
                    else if( !c.HasElements )
                    {
                        monitor.Warn( $"""
                                        Invalid element:
                                        {c}
                                        Is empty. Element is ignored.
                                        """ );
                    }
                    else
                    {
                        Process( monitor, c, world, p.AppendPart( name ), list, ref hasError );
                    }
                }
                else if( eN == _xRepository.LocalName )
                {
                    var aUrl = c.Attribute( _xUrl );
                    if( aUrl == null )
                    {
                        monitor.Error( $"""
                                        Invalid element:
                                        {c}
                                        Attribute Url=\"...\" is missing.
                                        """ );
                        hasError = true;
                    }
                    else
                    {
                        if( !Uri.TryCreate( aUrl.Value, UriKind.Absolute, out Uri? url ) )
                        {
                            // The Url is not an url.
                            // Enter "Repositories Proxy" mode.
                            if( IsValidFolderName( aUrl.Value ) && !world.Stack.LocalProxyRepositoriesPath.IsEmptyPath )
                            {
                                var candidate = world.Stack.LocalProxyRepositoriesPath.AppendPart( aUrl.Value );
                                if( Directory.Exists( candidate ) )
                                {
                                    url = new Uri( "file:///" + candidate, UriKind.Absolute );
                                    monitor.Trace( $"Automatic Repository Proxy mapping of Url=\"{aUrl.Value}\" to '{url}'." );
                                }
                            }
                            if( url == null )
                            {
                                monitor.Error( $"""
                                                Invalid element:
                                                {c}
                                                Attribute Url=\"{url}\" is invalid.
                                                """ );
                                hasError = true;
                            }
                        }
                        if( url != null )
                        {
                            // This removes any trailing .git, checks that no ?query part exists
                            // and extracts a necessarily valid repoName.
                            url = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( monitor, url, out var repoName );
                            if( url == null )
                            {
                                hasError = true;
                            }
                            else
                            {
                                list.Add( (p.AppendPart( repoName ), url) );
                            }
                        }
                    }
                }
                else if( eN != _xPlugins.LocalName )
                {
                    monitor.Warn( $"""
                        Unexpected element:
                        {c}
                        Only <Folder Name="..."> ... </Folder> and <Repository Url="..." /> are handled. Element is ignored.
                        """ );
                }
            }
        }

    }

}
