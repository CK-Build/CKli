using CK.Core;
using CKli.Core;
using System.Collections.Generic;
using System.Xml.Linq;

using System;
using System.IO;

namespace CKli.Core;

/// <summary>
/// Helper around a World.xml definition file.
/// </summary>
public sealed class WorldDefinitionFile
{
    readonly XElement _root;
    List<(NormalizedPath, Uri)>? _layout;

    /// <summary>
    /// Initalizes a new <see cref="WorldDefinitionFile"/> on a Xml document.
    /// The document can no more be altered.
    /// </summary>
    /// <param name="document">The xml document.</param>
    public WorldDefinitionFile( XDocument document )
    {
        Throw.CheckNotNullArgument( document );
        Throw.CheckArgument( document.Root is not null );
        _root = document.Root;
        _root.Document!.Changing += OnDocumentChanging;

        static void OnDocumentChanging( object? sender, XObjectChangeEventArgs e )
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
    /// The list is ordered by the Path.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The layout of the repositories or null if errors have been detected.</returns>
    public IReadOnlyList<(NormalizedPath Path, Uri Uri)>? ReadLayout( IActivityMonitor monitor )
    {
        return _layout ??= GetRepositoryLayout( monitor, _root );
    }

    static readonly XName _xPath = XNamespace.None + "Path";
    static readonly XName _xUrl = XNamespace.None + "Url";

    static List<(NormalizedPath Path, Uri Uri)>? GetRepositoryLayout( IActivityMonitor monitor, XElement root )
    {
        var list = new List<(NormalizedPath Path, Uri Uri)>();
        bool hasError = false;
        Process( monitor, root, default, list, ref hasError );
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
            if( path.Parts.Count > 1 )
            {
                var repoName = $"repository name '{path.LastPart}'";
                if( !uniqueCheck.TryAdd( repoName, url ) )
                {
                    monitor.Error( $"Repository url '{url}' and '{uniqueCheck[path]}' have the same '{repoName}'." );
                    hasError = true;
                }
            }
            if( !uniqueUrl.Add( url ) )
            {
                monitor.Error( $"Repository with Url '{url}' occurs more than once." );
                hasError = true;
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
                if( eN == "Folder" )
                {
                    NormalizedPath path = c.Attribute( _xPath )?.Value;
                    if( path.IsEmptyPath || path.IsRooted )
                    {
                        monitor.Error( $"""
                                        Invalid element:
                                        {c}
                                        Attribute Path=\"...\" is missing or invalid.
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
                        Process( monitor, c, p.Combine( path ), list, ref hasError );
                    }
                }
                else if( eN == "Repository" )
                {
                    var uri = c.Attribute( _xUrl )?.Value;
                    if( !Uri.TryCreate( uri, UriKind.Absolute, out var url ) )
                    {
                        monitor.Error( $"""
                                        Invalid element:
                                        {c}
                                        Attribute Url=\"...\" is missing or invalid.
                                        """ );
                        hasError = true;
                    }
                    else
                    {
                        // This removes any trailing .git, checks that no ?query part exists
                        // and extracts a necessarily valid stackName.
                        url = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( monitor, url, out var stackNameFromUrl );
                        if( url == null )
                        {
                            hasError = true;
                        }
                        else
                        {
                            list.Add( (p.AppendPart( stackNameFromUrl ), url) );
                        }
                    }
                }
                else
                {
                    monitor.Warn( $"""
                        Unexpected element:
                        {c}
                        Only <Folder Path="..."> ... </Folder> and <Repository Url="..." /> are handled. Element is ignored.
                        """ );
                }
            }
        }

    }

}
