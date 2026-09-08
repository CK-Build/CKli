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
    /// <summary>
    /// Describes <see cref="WorldName.IsValidLTSName(ReadOnlySpan{char})"/> for the user.
    /// </summary>
    internal const string InvalidLTSNameMessage = "Must be at least 3 characters that starts with '@', "
                                                  + "only ASCII lowercase characters, digits, - (hyphen), _ (underscore) and '.' (dot).";

    static Func<IActivityMonitor,string,string>? _repositoryUrlHook;
    readonly XElement _root;
    readonly XElement _plugins;
    readonly List<XElement> _references;
    readonly LocalWorldName _world;
    List<World.RepoLayout>? _layout;
    Dictionary<XName, (XElement Config, bool IsDisabled)>? _pluginsConfiguration;
    PluginCompileMode? _compileMode;
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

    WorldDefinitionFile( LocalWorldName world, XElement root, XElement plugins, List<XElement> references )
    {
        Throw.DebugAssert( root.Document != null );
        _root = root;
        _plugins = plugins;
        _references = references;
        _root.Document.Changed += OnDocumentChanged;
        _world = world;
    }

    void OnDocumentChanged( object? sender, XObjectChangeEventArgs e )
    {
        if( _allowEdit )
        {
            _isDirty = true;
        }
        else
        {
            Throw.InvalidOperationException( "Xml Definition file must not be changed." );
        }
    }

    /// <summary>
    /// Gets the world defined by this file.
    /// </summary>
    public LocalWorldName World => _world;

    /// <summary>
    /// Gets the root element.
    /// Must not be mutated otherwise a <see cref="InvalidOperationException"/> is raised.
    /// <para>
    /// This root carries the optional "MinCKliVersion" attribute. There is no API to change the "MinCKliVersion".
    /// It must be done by code or manually.
    /// </para>
    /// </summary>
    public XElement XmlRoot => _root;

    /// <summary>
    /// Gets the &lt;Plugins /&gt; element.
    /// Must not be mutated otherwise a <see cref="InvalidOperationException"/> is raised.
    /// <para>
    /// For advanced scenarii, <see cref="RawEditPlugins(IActivityMonitor, Action{IActivityMonitor, XElement})"/>
    /// can be used.
    /// </para>
    /// </summary>
    public XElement Plugins => _plugins;

    /// <summary>
    /// Gets the &lt;Reference Url="..." /&gt; elements of this world: the other Stacks that this world uses.
    /// They can be direct children of the root or be grouped in an optional &lt;References&gt; element.
    /// Must not be mutated otherwise a <see cref="InvalidOperationException"/> is raised.
    /// <para>
    /// A reference is honored by the "ckli clone" command only: it clones (or checks that it is already cloned)
    /// the referenced Stack next to this one, recursively. The optional DefaultClone attribute (that defaults
    /// to true) drives this and can be overridden by the --with-ref-clone and --without-ref-clone flags.
    /// </para>
    /// <para>
    /// A referenced Stack is public unless it has a Private="true" attribute. A public Stack cannot reference a
    /// private one: this is an error that prevents this world to be loaded.
    /// </para>
    /// </summary>
    public IReadOnlyList<XElement> References => _references;

    /// <summary>
    /// Gets &lt;Plugins CompileMode="..." /&gt;.
    /// It must exactly be set to "Debug" or "None", any other value (including the lack of attribute)
    /// is <see cref="PluginCompileMode.Release"/>.
    /// </summary>
    public PluginCompileMode CompileMode
    {
        get
        {
            if( !_compileMode.HasValue )
            {
                _compileMode = _plugins.Attribute( XNames.CompileMode )?.Value switch
                {
                    "Debug" => PluginCompileMode.Debug,
                    "None" => PluginCompileMode.None,
                    _ => PluginCompileMode.Release
                };
            }
            return _compileMode.Value;
        }
    }

    /// <summary>
    /// Reads the &lt;Plugins&gt; section.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The plugins configuration or null if errors have been detected.</returns>
    public IReadOnlyDictionary<XName,(XElement Config, bool IsDisabled)>? ReadPluginsConfiguration( IActivityMonitor monitor )
    {
        return _pluginsConfiguration ??= DoReadPluginsConfiguration( monitor );
    }

    Dictionary<XName, (XElement Config, bool IsDisabled)>? DoReadPluginsConfiguration( IActivityMonitor monitor )
    {
        bool success = true;
        var config = new Dictionary<XName, (XElement Config, bool IsDisabled)>();
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
                config.Add( name, (e, (bool?)e.Attribute( XNames.Disabled ) is true) );
            }
        }
        return success ? config : null;
    }

    /// <summary>
    /// Enables or disables a plugin.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="name">The plugin to enable.</param>
    /// <param name="enable">True to enable, false to disable.</param>
    /// <returns>True on success, false on error.</returns>
    public bool EnablePlugin( IActivityMonitor monitor, string name, bool enable )
    {
        var config = ReadPluginsConfiguration( monitor );
        if( config == null ) return false;
        if( !PluginMachinery.EnsureFullPluginName( monitor, name, out string? shortPluginName, out var longPluginName ) )
        {
            return false;
        }
        XElement? e = config.GetValueOrDefault( name ).Config;
        if( e == null )
        {
            monitor.Error( $"Unable to find Plugin configuration '{shortPluginName}'." );
            return false;
        }
        if( (bool?)_plugins.Attribute( XNames.Disabled ) is true == !enable )
        {
            return true;
        }
        using( StartEdit() )
        {
            e.SetAttributeValue( XNames.Disabled, enable ? null : "true" );
        }
        return SaveFile( monitor )
               && _world.Stack.Commit( monitor, $"{(enable ? "En" : "Dis")}abling '{shortPluginName}' plugin." );
    }

    /// <summary>
    /// Finds the <see cref="References"/> that match a stack name or url.
    /// <para>
    /// The <paramref name="nameOrUrl"/> can be the reference url, the referenced repository name ("XXX-Stack")
    /// or the stack name ("XXX"). Comparisons are case insensitive.
    /// </para>
    /// <para>
    /// More than one element can be returned when 2 references share the same stack name (on 2 different
    /// hosts): only the url can disambiguate them.
    /// </para>
    /// </summary>
    /// <param name="nameOrUrl">The reference url, repository name or stack name.</param>
    /// <returns>The matching references (may be empty).</returns>
    public IReadOnlyList<XElement> FindReferences( string nameOrUrl )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( nameOrUrl );
        return _references.Where( e => MatchReference( e, nameOrUrl ) ).ToList();
    }

    static bool MatchReference( XElement e, string nameOrUrl )
    {
        var sUrl = e.Attribute( XNames.Url )?.Value;
        if( sUrl == null ) return false;
        // The raw string comparison comes first: a hand written Url may not be a valid url and
        // "ckli world reference remove" must be able to remove such a reference.
        if( sUrl.Equals( nameOrUrl, StringComparison.OrdinalIgnoreCase ) ) return true;
        if( Uri.TryCreate( sUrl, UriKind.Absolute, out var url )
            && Uri.TryCreate( nameOrUrl, UriKind.Absolute, out var candidate )
            && GitRepositoryKey.OrdinalIgnoreCaseUrlEqualityComparer.Equals( url, candidate ) )
        {
            return true;
        }
        // Matches the repository name ("XXX-Stack") or the stack name ("XXX").
        return GitRepositoryKey.IsStackNamed( Path.GetFileName( sUrl.AsSpan() ), nameOrUrl );
    }

    /// <summary>
    /// Creates or updates a &lt;Reference Url="..." /&gt; element, saves this file and commits the change
    /// in the Stack repository.
    /// <para>
    /// This merges: a null <paramref name="defaultClone"/>, <paramref name="isPrivate"/> or <paramref name="ltsName"/>
    /// leaves the corresponding attribute as it is (absent when the reference is created). Since the 2 booleans default
    /// to true and false respectively, a true <paramref name="defaultClone"/> and a false <paramref name="isPrivate"/>
    /// remove them. LTSName has no default value: the empty string removes it.
    /// </para>
    /// <para>
    /// A new element is added to the last &lt;References&gt; element when this file has one, as a direct child
    /// of the root otherwise.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="url">The referenced Stack url.</param>
    /// <param name="defaultClone">The DefaultClone attribute value (null to leave it as it is).</param>
    /// <param name="isPrivate">The Private attribute value (null to leave it as it is).</param>
    /// <param name="ltsName">
    /// The LTSName attribute value: a valid <see cref="WorldName.IsValidLTSName(ReadOnlySpan{char})"/>, the empty
    /// string to remove the attribute, or null to leave it as it is.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    public bool SetReference( IActivityMonitor monitor, Uri url, bool? defaultClone, bool? isPrivate, string? ltsName = null )
    {
        GitRepositoryKey.ThrowArgumentExceptionOnInvalidUrl( url );
        Throw.CheckArgument( ltsName == null || ltsName.Length == 0 || WorldName.IsValidLTSName( ltsName ) );

        var found = FindReferences( url.ToString() );
        if( found.Count > 1 )
        {
            monitor.Error( $"""
                Duplicate <Reference Url="{url}" /> found in world '{_world.FullName}':
                {found.Select( e => e.ToString() ).Concatenate( Environment.NewLine )}
                They must be manually fixed.
                """ );
            return false;
        }
        var e = found.Count == 1 ? found[0] : null;
        // A public Stack cannot reference a private one: ReadReferences throws on this, so writing it
        // would produce a world definition file that can no more be loaded (and no more be fixed by
        // the "ckli world reference remove" command).
        bool willBePrivate = isPrivate ?? (e != null && (bool?)e.Attribute( XNames.Private ) is true);
        if( willBePrivate && _world.Stack.IsPublic )
        {
            monitor.Error( $"""
                Cannot reference the private Stack '{url}': the Stack '{_world.Stack.StackName}' is public
                and a public Stack cannot reference a private one.
                """ );
            return false;
        }
        using( StartEdit() )
        {
            if( e == null )
            {
                e = new XElement( XNames.Reference, new XAttribute( XNames.Url, url.ToString() ) );
                // Keeps the existing grouping: the last <References> element when there is one.
                var group = _root.Elements( XNames.References ).LastOrDefault() ?? _root;
                group.Add( e );
            }
            if( defaultClone.HasValue )
            {
                e.SetAttributeValue( XNames.DefaultClone, defaultClone.Value ? null : "false" );
            }
            if( isPrivate.HasValue )
            {
                e.SetAttributeValue( XNames.Private, isPrivate.Value ? "true" : null );
            }
            if( ltsName != null )
            {
                e.SetAttributeValue( XNames.LTSName, ltsName.Length == 0 ? null : ltsName );
            }
            RefreshReferences();
        }
        return SaveFile( monitor )
               && _world.Stack.Commit( monitor, $"Set reference to Stack '{url}' in world '{_world.FullName}'." );
    }

    /// <summary>
    /// Removes a &lt;Reference /&gt; element, saves this file and commits the change in the Stack repository.
    /// <para>
    /// When no reference matches, a warning is emitted and true is returned: removing a reference is idempotent.
    /// A &lt;References&gt; element that becomes empty is removed.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="nameOrUrl">The reference url, repository name or stack name (see <see cref="FindReferences(string)"/>).</param>
    /// <returns>True on success, false on error.</returns>
    public bool RemoveReference( IActivityMonitor monitor, string nameOrUrl )
    {
        var found = FindReferences( nameOrUrl );
        if( found.Count == 0 )
        {
            monitor.Warn( $"No <Reference /> matching '{nameOrUrl}' in world '{_world.FullName}'." );
            return true;
        }
        if( found.Count > 1 )
        {
            monitor.Error( $"""
                '{nameOrUrl}' matches {found.Count} references in world '{_world.FullName}':
                {found.Select( e => e.ToString() ).Concatenate( Environment.NewLine )}
                Their url must be used to remove one of them.
                """ );
            return false;
        }
        var e = found[0];
        var url = e.Attribute( XNames.Url )!.Value;
        using( StartEdit() )
        {
            var parent = e.Parent;
            Throw.DebugAssert( parent != null );
            e.Remove();
            // Mirrors RemoveRepository( removeEmptyFolder: true ): a <References> group that has nothing
            // left in it is removed.
            if( parent != _root && !parent.HasElements )
            {
                parent.Remove();
            }
            RefreshReferences();
        }
        return SaveFile( monitor )
               && _world.Stack.Commit( monitor, $"Removed reference to Stack '{url}' from world '{_world.FullName}'." );
    }

    void RefreshReferences()
    {
        Throw.DebugAssert( _allowEdit );
        _references.Clear();
        _references.AddRange( CollectReferences( _root ) );
    }

    /// <summary>
    /// Shallow and quick analysis of the &lt;Folder&gt; and &lt;Repository&gt; elements that
    /// checks the unicity of "Path" and "Url" attribute.
    /// <para>
    /// The sort order of this list drives the <see cref="Repo.Index"/> that is used by <see cref="RepoPluginBase{T}"/>
    /// to associate extra information to the Repo.
    /// By default this list keeps the order of the definition file. This can be changed by the static <see cref="RepoOrder"/>
    /// configuration (this is currently unused).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The layout of the repositories or null if errors have been detected.</returns>
    public IReadOnlyList<World.RepoLayout>? ReadLayout( IActivityMonitor monitor )
    {
        return _layout ??= GetRepositoryLayout( monitor, _root, _world );
    }

    /// <summary>
    /// Enables low level direct manipulation of the <see cref="Plugins"/>.
    /// Should be used with care.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="editor">The editor function to apply.</param>
    public void RawEditPlugins( IActivityMonitor monitor, Action<IActivityMonitor,XElement> editor )
    {
        using( StartEdit() )
        {
            editor( monitor, _plugins );
        }
    }

    /// <summary>
    /// Gets whether this file has been modified (<see cref="SaveFile(IActivityMonitor)"/> will be
    /// called by <see cref="StackRepository.Close(IActivityMonitor)"/>).
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Saves this file if <see cref="IsDirty"/> is true otherwise does nothing.
    /// <para>
    /// This should rarely be called directly: <see cref="StackRepository.Close(IActivityMonitor)"/> automatically
    /// saves its <see cref="World.DefinitionFile"/> if it has been modified.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>True on success, false on error.</returns>
    public bool SaveFile( IActivityMonitor monitor )
    {
        Throw.CheckState( "The definition file is being edited.", !_allowEdit );
        if( _isDirty )
        {
            var path = _world.XmlDescriptionFilePath;
            try
            {
                _root.Document!.SafeSave( path );
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

    // World.XifLayout uses this before multiple calls to Remove/AddRepository.
    internal IDisposable StartEdit()
    {
        Throw.DebugAssert( !_allowEdit );
        _allowEdit = true;
        return Util.CreateDisposableAction( () => _allowEdit = false );
    }

    internal void SetPluginCompileMode( IActivityMonitor monitor, PluginCompileMode mode )
    {
        using( StartEdit() )
        {
            _plugins.SetAttributeValue( XNames.CompileMode, mode != PluginCompileMode.Release ? mode.ToString() : null );
            _compileMode = mode;
        }
    }

    internal void EnsurePluginConfiguration( IActivityMonitor monitor, string shortPluginName )
    {
        var config = Plugins.Elements().FirstOrDefault( e => e.Name.LocalName.Equals( shortPluginName, StringComparison.OrdinalIgnoreCase ) );
        if( config == null )
        {
            using( StartEdit() )
            {
                Plugins.Add( new XElement( shortPluginName ) );
                _pluginsConfiguration = null;
            }
        }
    }

    internal void RemovePluginConfiguration( IActivityMonitor monitor, string shortPluginName )
    {
        // Take no risk, don't use the _pluginsConfiguration: analyze the xml (case insensitively) to find the configurations.
        var filter = (XElement e) => e.Name.LocalName.Equals( shortPluginName, StringComparison.OrdinalIgnoreCase );
        var allConfigs = _root.Descendants( XNames.Repository ).SelectMany( r => r.Elements().Where( filter ) )
                              .Concat( Plugins.Elements().Where( filter ) )
                              .ToList();

        if( allConfigs.Count > 0 )
        {
            using( StartEdit() )
            {
                allConfigs.Remove();
                _pluginsConfiguration = null;
            }
        }
    }

    /// <summary>
    /// Called by <see cref="World.AddRepositoryAsync"/> and <see cref="World.XifLayout(IActivityMonitor)"/> (StartEdit is already called).
    /// </summary>
    internal bool AddRepository( IActivityMonitor monitor, IEnumerable<string> folders, Uri uri, XElement? element )
    {
        Throw.DebugAssert( folders.All( IsValidFolderName ) );
        Throw.DebugAssert( GitRepositoryKey.GetRepositoryUrlError( uri ) == null );

        // Element is provided when:
        // - called by Xif and the repository must move.
        // - AddRepositoryAsync (RepoAddedEvent may have edited it).
        var isEditingAbove = _allowEdit;
        element ??= CreateDefaultRepositoryElement( monitor, uri );

        using( isEditingAbove ? null : StartEdit() )
        {
            XElement folder = EnsureFolder( folders, _root );
            folder.Add( element );
        }
        return isEditingAbove || SaveFile( monitor );

        static XElement EnsureFolder( IEnumerable<string> folders, XElement root )
        {
            XElement existing = root;
            bool found = true;
            var e = folders.GetEnumerator();
            while( e.MoveNext() )
            {
                var f = existing.Elements( XNames.Folder )
                                .FirstOrDefault( f => f.Attributes()
                                                       .Any( a => a.Name == XNames.Name
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
                    var newOne = new XElement( XNames.Folder, new XAttribute( XNames.Name, e.Current ) );
                    var nextName = existing.Elements( XNames.Folder ).FirstOrDefault( x => x.Attribute( XNames.Name )!.Value.CompareTo( e.Current ) > 0 );
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

    internal XElement CreateDefaultRepositoryElement( IActivityMonitor monitor, Uri uri )
    {
        // When element must be created, it only has a normalized url.
        // For the "Repository Proxy" url, it is the name of the Repo (the path.LastPart) in the Stack.LocalProxyRepositoriesPath.
        return new XElement( XNames.Repository, new XAttribute( XNames.Url, NormalizeRepositoryProxyUrl( monitor, uri ) ) );
    }

    /// <summary>
    /// Called by <see cref="World.RemoveRepository"/> and <see cref="World.XifLayout(IActivityMonitor)"/> (StartEdit is already called).
    /// </summary>
    internal bool RemoveRepository( IActivityMonitor monitor, Uri uri, bool removeEmptyFolder )
    {
        Throw.DebugAssert( _layout != null );

        string urlValue = NormalizeRepositoryProxyUrl( monitor, uri );
        var node = _root.Descendants( XNames.Repository )
                        .FirstOrDefault( e => e.Attribute( XNames.Url )?.Value == urlValue );
        if( node == null )
        {
            monitor.Error( $"""
                Unable to find <Repository Url="{uri}" /> in '{_world.FullName}' definition file:
                {_root}
                """ );
            return false;
        }
        var isEditingAbove = _allowEdit;
        using( isEditingAbove ? null : StartEdit() )
        {
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
        }
        return isEditingAbove || SaveFile( monitor );
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
        _root.Descendants( XNames.Folder ).Where( e => !e.HasElements ).Remove();
    }

    internal static WorldDefinitionFile Create( IActivityMonitor monitor, LocalWorldName world, XElement root )
    {
        var plugins = root.Ensure( XNames.Plugins, addFirst: true );
        if( _repositoryUrlHook != null )
        {
            foreach( var a in root.Descendants( XNames.Repository ).Attributes( XNames.Url ) )
            {
                if( a.Value != null ) a.Value = _repositoryUrlHook( monitor, a.Value );
            }
        }
        return new WorldDefinitionFile( world, root, plugins, ReadReferences( monitor, world, root ) );
    }

    /// <summary>
    /// Collects the &lt;Reference /&gt; elements in document order without any validation.
    /// This is used to refresh the <see cref="References"/> after an edit: the file has necessarily
    /// been loaded (and validated) by <see cref="ReadReferences"/> before any edit can occur.
    /// </summary>
    static IEnumerable<XElement> CollectReferences( XElement root )
    {
        foreach( var e in root.Elements() )
        {
            if( e.Name == XNames.Reference )
            {
                yield return e;
            }
            else if( e.Name == XNames.References )
            {
                foreach( var r in e.Elements( XNames.Reference ) )
                {
                    yield return r;
                }
            }
        }
    }

    /// <summary>
    /// Collects the &lt;Reference /&gt; elements (direct children of the root or children of the optional
    /// &lt;References&gt; element) and validates their attributes: an invalid one throws and this prevents
    /// the world to be loaded.
    /// </summary>
    static List<XElement> ReadReferences( IActivityMonitor monitor, LocalWorldName world, XElement root )
    {
        List<XElement>? references = null;
        foreach( var e in root.Elements() )
        {
            if( e.Name == XNames.Reference )
            {
                Add( world, e, ref references );
            }
            else if( e.Name == XNames.References )
            {
                foreach( var r in e.Elements() )
                {
                    if( r.Name != XNames.Reference )
                    {
                        monitor.Warn( $"""
                            Unexpected element:
                            {r}
                            Only <Reference Url="..." /> is handled in <References>. Element is ignored.
                            """ );
                    }
                    else
                    {
                        Add( world, r, ref references );
                    }
                }
            }
        }
        return references ?? new List<XElement>();

        static void Add( LocalWorldName world, XElement e, ref List<XElement>? references )
        {
            // Reads the 2 optional boolean attributes here: an invalid value throws (the world cannot be loaded)
            // instead of failing later in the "ckli clone" command that consumes them.
            _ = (bool?)e.Attribute( XNames.DefaultClone );
            // Same for the optional LTSName: it has no default value (when absent, the referenced Stack's
            // default world is the one that is used) but an invalid one must not reach any consumer.
            var ltsName = e.Attribute( XNames.LTSName )?.Value;
            if( ltsName != null && !WorldName.IsValidLTSName( ltsName ) )
            {
                Throw.CKException( $"""
                    Invalid element:
                    {e}
                    Invalid LTSName="{ltsName}". {InvalidLTSNameMessage}
                    """ );
            }
            if( (bool?)e.Attribute( XNames.Private ) is true && world.Stack.IsPublic )
            {
                Throw.CKException( $"""
                    Invalid element:
                    {e}
                    A public Stack cannot reference a private one.
                    """ );
            }
            references ??= new List<XElement>();
            references.Add( e );
        }
    }

    static List<World.RepoLayout>? GetRepositoryLayout( IActivityMonitor monitor,
                                                        XElement root,
                                                        LocalWorldName world )
    {
        var list = new List<World.RepoLayout>();
        bool hasError = false;

        NormalizedPath worldRoot = world.WorldRoot;
        Process( monitor, root, world, worldRoot, list, isRoot: true, ref hasError );
        if( hasError ) return null;
        var uniqueCheck = new Dictionary<string,Uri>();
        var uniquePath = new HashSet<NormalizedPath>();
        var uniqueUrl = new HashSet<Uri>();
        foreach( var (url, _, path) in list )
        {
            // These 2 checks guaranties that path <-> url is unique
            // and that repo name (the path.LastPart) is also unique.
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
                             List<World.RepoLayout> list,
                             bool isRoot,
                             ref bool hasError )
        {
            foreach( var c in e.Elements() )
            {
                var eN = c.Name.LocalName;
                if( eN == XNames.Folder.LocalName )
                {
                    string? name = c.Attribute( XNames.Name )?.Value;
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
                        Process( monitor, c, world, p.AppendPart( name ), list, isRoot: false, ref hasError );
                    }
                }
                else if( eN == XNames.Repository.LocalName )
                {
                    var aUrl = c.Attribute( XNames.Url );
                    if( aUrl == null )
                    {
                        monitor.Error( $"""
                                        Invalid element:
                                        {c}
                                        Attribute Url="..." is missing.
                                        """ );
                        hasError = true;
                    }
                    else
                    {
                        if( !Uri.TryCreate( aUrl.Value, UriKind.Absolute, out Uri? url ) )
                        {
                            // The Url is not an url.
                            // Enter "Repositories Proxy" mode.
                            if( !world.Stack.LocalProxyRepositoriesPath.IsEmptyPath )
                            {
                                bool foundMapping = false;
                                if( IsValidFolderName( aUrl.Value ) )
                                {
                                    var candidate = world.Stack.LocalProxyRepositoriesPath.AppendPart( aUrl.Value );
                                    if( Directory.Exists( candidate ) )
                                    {
                                        url = new Uri( candidate );
                                        monitor.Trace( $"Automatic Repository Proxy mapping of Url=\"{aUrl.Value}\" to '{url}'." );
                                        foundMapping = true;
                                    }
                                }
                                if( !foundMapping )
                                {
                                    monitor.Error( $"""
                                                Invalid element:
                                                {c}
                                                The Url="{url}" cannot be mapped to any folder in the local proxy repository.
                                                {world.Stack.LocalProxyRepositoriesPath} contains the directories:
                                                {Directory.EnumerateDirectories( world.Stack.LocalProxyRepositoriesPath ).Concatenate()}
                                                """ );
                                    hasError = true;
                                }
                            }
                            else
                            {
                                monitor.Error( $"""
                                                Invalid element:
                                                {c}
                                                The Url="{url}" is not a valid url.
                                                """ );
                                hasError = true;
                            }
                        }
                        if( url != null )
                        {
                            // This removes any trailing .git, checks that no ?query part exists
                            // and extracts a necessarily valid repoName.
                            var urlError = GitRepositoryKey.GetRepositoryUrlError( url );
                            if( urlError != null )
                            {
                                monitor.Error( urlError );
                                hasError = true;
                            }
                            else
                            {
                                var repoName = Path.GetFileName( url.ToString().AsSpan() );
                                list.Add( new World.RepoLayout( url, c, p.AppendPart( new string( repoName ) ) ) );
                            }
                        }
                    }
                }
                else if( eN != XNames.Plugins.LocalName
                         // The references (see ReadReferences) are not part of the layout: they are
                         // handled by the "ckli clone" command only.
                         && !(isRoot && (eN == XNames.References.LocalName || eN == XNames.Reference.LocalName)) )
                {
                    monitor.Warn( $"""
                        Unexpected element:
                        {c}
                        Only <Plugins />, <References />, <Reference Url="..." />, <Folder Name="..."> ... </Folder>
                        and <Repository Url="..." /> are handled. Element is ignored.
                        """ );
                }
            }
        }

    }
}
