using CK.Core;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Helper around a World.xml definition file.
    /// </summary>
    public sealed class WorldDefinitionFile
    {
        readonly XElement _root;
        List<(NormalizedPath, Uri)>? _layout;

        WorldDefinitionFile( XElement root )
        {
            _root = root;
            _root.Document!.Changing += OnDocumentChange;
        }

        void OnDocumentChange( object? sender, XObjectChangeEventArgs e )
        {
            Throw.InvalidOperationException( "Xml document must not be changed." );
        }

        /// <summary>
        /// Gets the root element already preprocessed by <see cref="XTypedFactory.PreProcess(IActivityMonitor, XElement)"/>.
        /// Must not be mutated otherwise a <see cref="InvalidOperationException"/> is raised.
        /// </summary>
        public XElement Root => _root;

        /// <summary>
        /// Shallow and quick analysis of the &lt;GitFolder&gt; and &lt;Folder&gt; elements that
        /// checks the unicity of "Path" and "Url" attribute.
        /// The list is ordered by the Path.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The layout of the GitFolder or null if errors have been detected.</returns>
        public IReadOnlyList<(NormalizedPath Path, Uri Uri)>? ReadLayout( IActivityMonitor monitor )
        {
            return _layout ??= GetRepositoryLayout( monitor, _root );
        }

        public static WorldDefinitionFile? Load( IActivityMonitor monitor, XDocument document )
        {
            Throw.CheckArgument( document?.Root != null );
            var preProcessResult = XTypedFactory.PreProcess( monitor, document.Root );
            if( preProcessResult.Result == null || preProcessResult.Errors.Count > 0 ) return null;
            return new WorldDefinitionFile( document.Root );
        }

        static List<(NormalizedPath Path, Uri Uri)>? GetRepositoryLayout( IActivityMonitor monitor, XElement root )
        {
            var list = new List<(NormalizedPath Path, Uri Uri)>();
            bool hasError = false;
            Process( monitor, root, default, list, ref hasError );
            if( hasError ) return null;
            var uniquePath = new HashSet<NormalizedPath>();
            var uniqueUrl = new HashSet<Uri>();
            foreach( var (path, url) in list )
            {
                if( !uniquePath.Add( path ) )
                {
                    monitor.Error( $"GitFolder with resulting path '{path}' occurs more than once." );
                    hasError = true;
                }
                if( !uniqueUrl.Add( url ) )
                {
                    monitor.Error( $"GitFolder with Url '{url}' occurs more than once." );
                    hasError = true;
                }
            }
            if( hasError ) return null;
            list.Sort( ( e1, e2 ) => e1.Path.CompareTo( e2.Path ) );
            return hasError ? null : list;

            static void Process( IActivityMonitor monitor, XElement e, in NormalizedPath p, List<(NormalizedPath, Uri)> list, ref bool hasError )
            {
                foreach( var c in e.Elements() )
                {
                    bool isFolder = c.Name.LocalName == "Folder";
                    if( (isFolder || c.Name.LocalName == "GitFolder") )
                    {
                        var name = c.Attribute( "Name" )?.Value;
                        if( string.IsNullOrWhiteSpace( name ) )
                        {
                            monitor.Warn( $"Element '{c}' missing non empty Name attribute. Skipped." );
                            hasError = true;
                        }
                        else
                        {
                            var pC = p.AppendPart( name );
                            if( isFolder ) Process( monitor, c, pC, list, ref hasError );
                            else
                            {
                                var urlString = c.Attribute( "Url" )?.Value;
                                if( !Uri.TryCreate( urlString, UriKind.Absolute, out var url ) )
                                {
                                    monitor.Warn( $"Element '{c}': missing absolute Url attribute. Skipped." );
                                    hasError = true;
                                }
                                else
                                {
                                    url = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( url );
                                    list.Add( (pC, url) );
                                }
                            }
                        }
                    }
                }
            }

        }

    }

}
