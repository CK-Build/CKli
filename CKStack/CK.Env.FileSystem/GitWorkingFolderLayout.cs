using CK.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CK.Core.CheckedWriteStream;

namespace CK.Env
{
    public sealed partial class GitWorkingFolderLayout
    {
        readonly NormalizedPath _root;
        readonly (NormalizedPath SubPath, Uri OriginUrl)[] _layout;
        readonly IReadOnlyList<NormalizedPath> _repositoryIssues;
        readonly bool _missingRoot;

        GitWorkingFolderLayout( NormalizedPath root, bool missingRoot, IReadOnlyList<NormalizedPath>? repositoryIssues, (NormalizedPath SubPath, Uri OriginUrl)[] layout )
        {
            _root = root;
            _missingRoot = missingRoot;
            _repositoryIssues = repositoryIssues ?? Array.Empty<NormalizedPath>();
            _layout = layout;
        }

        /// <summary>
        /// Gets the root of this layout.
        /// </summary>
        public NormalizedPath Root => _root;

        /// <summary>
        /// Gets the layouts, paths are relative to the <see cref="Root"/>.
        /// </summary>
        public IReadOnlyList<(NormalizedPath SubPath, Uri OriginUrl)> Layout => _layout;

        /// <summary>
        /// Gets whether <see cref="Root"/> folder is missing.
        /// </summary>
        public bool MissingRoot => _missingRoot;

        /// <summary>
        /// Gets whether any <see cref="RepositoryIssues"/>, <see cref="HomonymIssues"/> or <see cref="OriginUrlIssues"/> exist.
        /// </summary>
        public bool HasIssues => RepositoryIssues.Any() || HomonymIssues.Any() || OriginUrlIssues.Any();

        /// <summary>
        /// Gets the path that should be working folder since a .git/folder file exists but for which
        /// the origin url couldn't be read.
        /// This set should always be empty.
        /// </summary>
        public IReadOnlyList<NormalizedPath> RepositoryIssues => _repositoryIssues;

        /// <summary>
        /// Gets all the paths that share a common <see cref="NormalizedPath.LastPart"/> grouped by the LastPart.
        /// This set should always be empty.
        /// </summary>
        public IEnumerable<IGrouping<string, NormalizedPath>> HomonymIssues => _layout.Select( p => p.SubPath )
                                                                                      .GroupBy( p => p.LastPart, StringComparer.OrdinalIgnoreCase )
                                                                                      .Where( g => g.Count() > 1 );

        /// <summary>
        /// Gets all the paths that share a common origin url grouped by the origin url.
        /// This set should always be empty.
        /// </summary>
        public IEnumerable<IGrouping<Uri, NormalizedPath>> OriginUrlIssues => _layout.GroupBy( p => p.OriginUrl, p => p.SubPath ).Where( g => g.Count() > 1 );

        /// <summary>
        /// Creates a <see cref="GitWorkingFolderLayout"/> that lists top most working folders from a root path.
        /// When a repository's origin url cannot be read from an existing /.git/config file warning
        /// and an error (if an exception occurred) are logged, the folder is skipped and collected in <see cref="RepositoryIssues"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="root">The root path (it is not considered).</param>
        /// <param name="ignore">Optional predicate that can skip folders entirely.</param>
        /// <returns>The layout of the working folders or null on severe error.</returns>
        public static GitWorkingFolderLayout? Create( IActivityMonitor monitor,
                                                      NormalizedPath root,
                                                      Func<NormalizedPath, bool>? ignore = null )
        {
            using( monitor.OpenTrace( $"Creating Git working folder layout for '{root}'." ) )
            {
                try
                {
                    if( Directory.Exists( root ) )
                    {
                        var result = new List<(NormalizedPath SubPath, Uri OriginUrl)>();
                        List<NormalizedPath>? issues = null;
                        Collect( monitor, root.Path.Length + 1, root, result, ref issues, ignore );
                        return new GitWorkingFolderLayout( root, true, issues, result.ToArray() );
                    }
                    else
                    {
                        return new GitWorkingFolderLayout( root, false, null, Array.Empty<(NormalizedPath, Uri)>() );
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( $"While creating Git working folder layout for '{root}'.", ex );
                    return null;
                }
            }

            static void Collect( IActivityMonitor monitor,
                                 int rootPathLength,
                                 NormalizedPath parent,
                                 List<(NormalizedPath SubPath, Uri OriginUrl)> result,
                                 ref List<NormalizedPath>? issues,
                                 Func<NormalizedPath, bool>? ignore )
            {
                if( ignore?.Invoke( parent ) is true )
                {
                    monitor.Trace( $"Ignoring '{parent}'." );
                    return;
                }
                foreach( var path in Directory.EnumerateDirectories( parent ) )
                {
                    var gitConfig = $"{path}{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}.git/config";
                    if( File.Exists( gitConfig ) )
                    {
                        var url = QuickRead( gitConfig ) ?? LibGitRead( monitor, path, gitConfig );
                        var p = new NormalizedPath( path.Substring( rootPathLength ) );
                        if( url == null )
                        {
                            monitor.Warn( $"Skipping '{p}' sub folders since the remote \"origin\" url cannot be read from '{gitConfig}' file. Adding it to the RepositoryIssues set." );
                            issues ??= new List<NormalizedPath>();
                            issues.Add( p );
                        }
                        else
                        {
                            result.Add( (p, url) );
                        }
                    }
                    else
                    {
                        Collect( monitor, rootPathLength, path, result, ref issues, ignore );
                    }
                }

                static Uri? QuickRead( string gitConfig )
                {
                    var cfg = File.ReadAllText( gitConfig );
                    int idx = cfg.IndexOf( "[remote \"origin\"]" );
                    if( idx >= 0 )
                    {
                        idx = cfg.IndexOf( "url = \"", idx + 17 );
                        if( idx > 0 )
                        {
                            int beg = idx + 7;
                            idx = cfg.IndexOf( '"', beg );
                            if( idx > 0
                                && cfg.AsSpan( idx - 4 ).Equals( ".git", StringComparison.OrdinalIgnoreCase )
                                && Uri.TryCreate( cfg.Substring( beg, idx - beg - 4 ), UriKind.RelativeOrAbsolute, out var uri ) )
                            {
                                return uri;
                            }
                        }
                    }
                    return null;
                }

                static Uri? LibGitRead( IActivityMonitor monitor, string path, string gitConfig )
                {
                    var r = new Repository( path );
                    try
                    {
                        var u = r.Network.Remotes["origin"].Url;
                        if( !Uri.TryCreate( u, UriKind.RelativeOrAbsolute, out var url ) )
                        {
                            monitor.Warn( $"Invalid remote \"origin\" url '{u}'. It must be absolute." );
                        }
                        return url;
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( $"While reading \"origin\" url from '{gitConfig}'.", ex );
                    }
                    finally
                    {
                        r.Dispose();
                    }
                    return null;
                }
            }

        }

    }
}
