using CK.Core;
using CK.Text;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CK.Env
{
    /// <summary>
    /// Encapsulates a whole context.
    /// </summary>
    public sealed partial class GitWorldStore
    {
        /// <summary>
        /// Simple encapsulation of a <see cref="GitRepositoryKey"/> that should contain xml definition files
        /// of <see cref="Worlds"/>.
        /// </summary>
        public class StackRepo : GitRepositoryKey, IDisposable
        {
            readonly GitWorldStore _store;
            GitRepository _git;
            readonly List<WorldInfo> _worlds;

            internal StackRepo( GitWorldStore store, Uri uri, bool isPublic, string branchName = null )
                : base( store.SecretKeyStore, uri, isPublic )
            {
                _store = store;
                BranchName = branchName ?? "master";
                var cleanPath = CleanPathDirName( uri.AbsolutePath );
                Root = store._rootPath.AppendPart( cleanPath );
                _worlds = new List<WorldInfo>();
            }

            internal static StackRepo Parse( GitWorldStore store, XElement e )
            {
                var uri = new Uri( (string)e.AttributeRequired( nameof( OriginUrl ) ) );
                var pub = (bool?)e.Attribute( nameof(IsPublic) ) ?? false;
                var r = new StackRepo( store, uri, pub, null );
                r._worlds.AddRange( e.Elements( "Worlds" ).Elements( nameof(WorldInfo) ).Select( eW => new WorldInfo( r, eW ) ) );
                return r;
            }

            internal XElement ToXml() => new XElement( nameof(StackRepo),
                                new XAttribute( nameof( OriginUrl ), OriginUrl ),
                                new XAttribute( nameof( IsPublic ), IsPublic ),
                                new XElement( "Worlds", _worlds.Select( w => w.ToXml() ) ) );


            /// <summary>
            /// Gets the root store.
            /// </summary>
            public GitWorldStore Store => _store;

            /// <summary>
            /// Gets or whether the Git repository is public.
            /// </summary>
            public new bool IsPublic { get; set; }

            /// <summary>
            /// Gets the branch name: Should always be "master" but this may be changed.
            /// </summary>
            public string BranchName { get; }

            internal void OnDispose( WorldInfo worldInfo )
            {
                _worlds.Remove( worldInfo );
            }

            /// <summary>
            /// Commits and push changes to the remote.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <returns>True on success, false on error.</returns>
            internal bool PushChanges( IActivityMonitor m )
            {
                Debug.Assert( IsOpen );
                return _git.Commit( m, "Automatic synchronization commit." ) && _git.Push( m );
            }

            /// <summary>
            /// Return true if the Git Repository is open.
            /// </summary>
            public bool IsOpen => _git != null;

            /// <summary>
            /// Gets the root path of this repository: it is a folder in <see cref="GitWorldStore.RootPath"/>
            /// that is derived from this <see cref="GitRepositoryKey.OriginUrl"/>.
            /// </summary>
            public NormalizedPath Root { get; }

            /// <summary>
            /// Ensures that the the Git Repository is opened and updates the <see cref="Worlds"/>.
            /// Returns true on success, false on error.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <param name="force">True to refresh the <see cref="Worlds"/> even if <see cref="IsOpen"/> is already true.</param>
            /// <returns>Whether this repository has been successfully opened.</returns>
            internal bool Refresh( IActivityMonitor m, bool force = true )
            {
                bool isOpened = false;
                if( !IsOpen )
                {
                    _git = GitRepository.Create( m, this, Root, Root.LastPart, false, BranchName, checkOutBranchName: true );
                    if( _git == null ) return false;
                    isOpened = true;
                }
                if( force || isOpened )
                {
                    if( _git.Pull( m, MergeFileFavor.Theirs ).ReloadNeeded || isOpened )
                    {
                        var worldNames = Directory.GetFiles( Root, "*.World.xml" )
                                                  .Select( p => LocalWorldName.TryParse( p, _store.WorldLocalMapping ) )
                                                  .Where( w => w != null )
                                                  .ToDictionary( w => w.FullName );

                        var invalidParallels = worldNames.Values.Where( p => p.ParallelName != null && !worldNames.ContainsKey( p.Name ) ).ToList();
                        foreach( var orphan in invalidParallels )
                        {
                            m.Warn( $"Invalid Parallel World '{orphan.FullName}': unable to find the default stack definition '{orphan.Name}' in the repository. It is ignored." );
                            worldNames.Remove( orphan.FullName );
                        }
                        foreach( var exists in _worlds )
                        {
                            if( !worldNames.Remove( exists.WorldName.FullName ) )
                            {
                                if( exists.HasDefinitionFile )
                                {
                                    m.Warn( $"Unable to find World definition file for '{exists.WorldName}'. File '{exists.WorldName.XmlDescriptionFilePath}' not found." );
                                    exists.HasDefinitionFile = false;
                                }
                            }
                            else
                            {
                                if( !exists.HasDefinitionFile )
                                {
                                    m.Trace( $"Found World definition file for '{exists.WorldName}'." );
                                    exists.HasDefinitionFile = true;
                                }
                            }
                        }
                        foreach( var newWorld in worldNames.Values )
                        {
                            m.Info( $"Found a new World definition: creating '{newWorld.FullName}' entry." );
                            _worlds.Add( new WorldInfo( this, newWorld, true ) );
                        }
                    }
                }
                return IsOpen;
            }

            /// <summary>
            /// Gets all the stacks that this repository defines (or should define: see <see cref="WorldInfo.HasDefinitionFile"/>).
            /// </summary>
            public IReadOnlyList<WorldInfo> Worlds => _worlds;

            public void Dispose()
            {
                if( IsOpen )
                {
                    _git.Dispose();
                    _git = null;
                }
            }

            /// <summary>
            /// This is public to be used by unit tests.
            /// </summary>
            /// <param name="path">The original path.</param>
            /// <returns>The cleaned up path.</returns>
            public static string CleanPathDirName( string path ) =>
                    path.Replace( ".git", "" )
                        .Replace( "_git", "" )
                        .Replace( '/', '_' )
                        .Replace( ':', '_' )
                        .Replace( "__", "_" )
                        .Trim( '_' )
                        .ToLowerInvariant();

        }

    }
}
