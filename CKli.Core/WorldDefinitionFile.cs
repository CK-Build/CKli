using CK.Core;
using System.Collections.Generic;
using System.Xml.Linq;
using System;
using System.Linq;

namespace CKli.Core;

/// <summary>
/// Helper around a World.xml definition file.
/// </summary>
public sealed class WorldDefinitionFile
{
    static Func<IActivityMonitor,string,string>? _repositoryUrlHook;
    readonly XElement _root;
    readonly LocalWorldName _world;
    List<(NormalizedPath, Uri)>? _layout;
    bool _allowEdit;
    bool _isDirty;

    internal WorldDefinitionFile( LocalWorldName world, XDocument document )
    {
        Throw.CheckNotNullArgument( document );
        Throw.CheckArgument( document.Root is not null );
        _root = document.Root;
        _root.Document!.Changing += OnDocumentChanging;
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
    /// Gets the root element.
    /// Must not be mutated otherwise a <see cref="InvalidOperationException"/> is raised.
    /// </summary>
    public XElement Root => _root;

    /// <summary>
    /// Shallow and quick analysis of the &lt;Folder&gt; and &lt;Repository&gt; elements that
    /// checks the unicity of "Path" and "Url" attribute.
    /// The list is ordered by the Path (that are absolute).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The layout of the repositories or null if errors have been detected.</returns>
    public IReadOnlyList<(NormalizedPath Path, Uri Uri)>? ReadLayout( IActivityMonitor monitor )
    {
        return _layout ??= GetRepositoryLayout( monitor, _root, _world.WorldRoot );
    }

    /// <summary>
    /// Gets or sets an optional transformer of the &lt;Repository Url="..." /&gt; value.
    /// <para>
    /// This is mainly for tests. Note that this is applied when loading the xml file definition:
    /// the in-memory representation is no more the same as the file. Saving the definition
    /// file is required for the file to be updated with the transformation.
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
        return Util.CreateDisposableAction( () =>
        {
            _allowEdit = false;
            _layout = null;
        } );
    }

    internal void AddRepository( IEnumerable<string> folders, Uri uri )
    {
        Throw.DebugAssert( folders.All( IsValidFolderName ) );
        Throw.DebugAssert( GitRepositoryKey.CheckAndNormalizeRepositoryUrl( uri ) == uri );
        Throw.DebugAssert( _allowEdit );

        XElement folder = EnsureFolder( folders, _root );
        folder.Add( new XElement( _xRepository, new XAttribute( _xUrl, uri.ToString() ) ) );

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
                    existing.Add( newOne );
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
        var node = _root.Descendants( _xRepository )
                        .FirstOrDefault( e => e.Attribute( _xUrl )?.Value == uri.ToString() );
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
                _root.Document!.Save( path );
                _isDirty = false;
                monitor.Trace( $"File '{path.LastPart}' saved." );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While saving '{path}'.", ex );
                return false;
            }
        }
        return true;
    }

    static readonly XName _xRepository = XNamespace.None + "Repository";
    static readonly XName _xFolder = XNamespace.None + "Folder";
    static readonly XName _xName = XNamespace.None + "Name";
    static readonly XName _xUrl = XNamespace.None + "Url";

    internal static void ApplyUrlHook( IActivityMonitor monitor, XElement e )
    {
        Throw.DebugAssert( _repositoryUrlHook != null );
        foreach( var a in e.Descendants( _xRepository ).Attributes( _xUrl ) )
        {
            if( a.Value != null ) a.Value = _repositoryUrlHook( monitor, a.Value );
        }
    }

    static List<(NormalizedPath Path, Uri Uri)>? GetRepositoryLayout( IActivityMonitor monitor, XElement root, NormalizedPath worldRoot )
    {
        var list = new List<(NormalizedPath Path, Uri Uri)>();
        bool hasError = false;
        Process( monitor, root, worldRoot, list, ref hasError );
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
        list.Sort( ( e1, e2 ) => e1.Path.CompareTo( e2.Path ) );
        return list;


        static void Process( IActivityMonitor monitor, XElement e, in NormalizedPath p, List<(NormalizedPath, Uri)> list, ref bool hasError )
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
                        Process( monitor, c, p.AppendPart( name ), list, ref hasError );
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
                            monitor.Error( $"""
                                        Invalid element:
                                        {c}
                                        Attribute Url=\"{url}\" is invalid.
                                        """ );
                            hasError = true;
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
                else
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
