using CK.Core;
using CK.SimpleKeyVault;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static CK.Env.GitWorldStore;

namespace CK.Env
{
    public sealed partial class GitWorldStore
    {
        /// <summary>
        /// Simple encapsulation of a <see cref="GitRepositoryKey"/> that should contain xml definition files
        /// of <see cref="Worlds"/>.
        /// </summary>
        public class StackRepo : GitRepositoryKey, IDisposable
        {
            readonly GitWorldStore _store;
            SimpleGitRepository? _git;
            readonly List<WorldInfo> _worlds;

            internal StackRepo( GitWorldStore store, Uri uri, bool isPublic, string? branchName = null )
                : base( store.SecretKeyStore, uri, isPublic )
            {
                _store = store;
                BranchName = branchName ?? IWorldName.MasterName;
                var cleanPath = CleanPathDirName( uri.AbsolutePath );
                Root = store._rootStorePath.AppendPart( cleanPath );
                _worlds = new List<WorldInfo>();
            }

            internal static StackRepo Parse( GitWorldStore store, XElement e, XElement? singleWorld = null )
            {
                var uri = new Uri( (string)e.AttributeRequired( nameof( OriginUrl ) ) );
                var pub = (bool?)e.Attribute( nameof( IsPublic ) ) ?? false;
                var r = new StackRepo( store, uri, pub, null );
                if( singleWorld == null )
                {
                    r._worlds.AddRange( e.Elements( "Worlds" ).Elements( nameof( WorldInfo ) ).Select( eW => new WorldInfo( r, eW ) ) );
                }
                else
                {
                    r._worlds.Add( new WorldInfo( r, singleWorld ) );
                }
                return r;
            }

            internal XElement ToXml() => new XElement( nameof( StackRepo ),
                                new XAttribute( nameof( OriginUrl ), OriginUrl ),
                                new XAttribute( nameof( IsPublic ), IsPublic ),
                                new XElement( "Worlds", _worlds.Select( w => w.ToXml() ) ) );

            /// <summary>
            /// Gets the root store.
            /// </summary>
            internal GitWorldStore Store => _store;

            /// <summary>
            /// Gets or whether the Git repository is public.
            /// </summary>
            /// <remarks>
            /// This non virtual (masked) implementation works around the impossibility for an overridden
            /// property to change the setter from public to protected.
            /// </remarks>
            public new bool IsPublic { get => base.IsPublic; set => base.IsPublic = value; }

            /// <summary>
            /// Gets the branch name: Should always be <see cref="IWorldName.MasterName"/> but this may be changed.
            /// </summary>
            public string BranchName { get; }

            internal void OnDestroy( WorldInfo worldInfo )
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
                Debug.Assert( _git != null );
                CommittingResult result = _git.Commit( m, "Automatic synchronization commit." );
                if( result == CommittingResult.Error ) return false;
                if(result == CommittingResult.NoChanges)
                {
                    m.Info( "Nothing commited. Skipping push." );
                    return true;
                }
                return _git.Push( m );
            }

            /// <summary>
            /// Return true if the Git Repository is open.
            /// </summary>
            internal bool IsOpen => _git != null;

            /// <summary>
            /// Gets the root path of this repository (the .Stack folder).
            /// </summary>
            public NormalizedPath Root { get; }

            /// <summary>
            /// Ensures that the Git Repository of this stack is opened (pulls it on opening) and checks that the single world file definition exists.
            /// The .World.xml definition file must exist.
            /// Returns true on success, false on error.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <param name="LocalWorldName">The local single name.</param>
            /// <returns>True on success, false otherwise.</returns>
            internal bool RefreshSingle( IActivityMonitor m, out LocalWorldName name )
            {
                Debug.Assert( _store.SingleWorld != null );
                Debug.Assert( _worlds.Count == 1 );
                name = _worlds[0].WorldName;
                if( !DoOpen( m, out var opened, out var localDir ) ) return false;
                Debug.Assert( _git != null );
                if( opened )
                {
                    // Ignores errors so we can work off line.
                    _git.Pull( m, MergeFileFavor.Theirs );
                }
                var f = _worlds[0].WorldName.XmlDescriptionFilePath;
                if( !File.Exists( f ) )
                {
                    m.Error( $"File '{f}' cannot be found." );
                    return false;
                }
                return true;
            }

            /// <summary>
            /// Ensures that the Git Repository of this stack is opened and updates the <see cref="Worlds"/> from the files.
            /// Returns true on success, false on error.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <param name="forcePullAndRefresh">True to pull and refresh the <see cref="Worlds"/> list even if <see cref="IsOpen"/> is already true.</param>
            /// <returns>Whether this repository has been successfully opened.</returns>
            internal (bool LoadSuccess, bool HasChanged) RefreshMultiple( IActivityMonitor m, bool forcePullAndRefresh )
            {
                Debug.Assert( _store.SingleWorld == null );
                if( !DoOpen( m, out var opened, out var localDir ) ) return (false, false);
                Debug.Assert( _git != null );
                bool hasChanged = false;
                if( forcePullAndRefresh || opened )
                {
                    if( _git.Pull( m, MergeFileFavor.Theirs ).ReloadNeeded || opened )
                    {
                        var worldNames = Directory.GetFiles( Root, "*.World.xml" )
                                                  .Select( p => LocalWorldName.TryParseOBSOLETE( p, _store.WorldLocalMapping ) )
                                                  .Where( w => w != null )
                                                  .ToDictionary( w => w!.FullName, w => w! );
                        // Ignore parallels that have a StackName that doesn't exist.
                        var invalidParallels = worldNames.Values.Where( p => p!.ParallelName != null && !worldNames.ContainsKey( p.Name ) ).ToList();
                        foreach( var orphan in invalidParallels )
                        {
                            m.Warn( $"Invalid Parallel World '{orphan.FullName}': unable to find the default stack definition '{orphan.Name}' in the repository. It is ignored." );
                            worldNames.Remove( orphan.FullName );
                        }
                        // Cleanup the worldNames built from the definition files with all the worlds that are already known.
                        foreach( var exists in _worlds )
                        {
                            if( !worldNames.Remove( exists.WorldName.FullName ) )
                            {
                                // The definition file has disappeared.
                                if( exists.WorldName.HasDefinitionFile )
                                {
                                    m.Warn( $"Unable to find World definition file for '{exists.WorldName}'. File '{exists.WorldName.XmlDescriptionFilePath}' not found." );
                                    exists.WorldName.HasDefinitionFile = false;
                                    hasChanged = true;
                                }
                            }
                            else
                            {
                                // The definition file exists.
                                if( !exists.WorldName.HasDefinitionFile )
                                {
                                    m.Trace( $"Found World definition file for '{exists.WorldName}'." );
                                    exists.WorldName.HasDefinitionFile = true;
                                    hasChanged = true;
                                }
                            }
                        }
                        // Process the remaining world definitions: these are necessarily new worlds in this stack...
                        foreach( LocalWorldName newWorld in worldNames.Values )
                        {
                            var alreadyDefiner = _store._stackRepos.FirstOrDefault( r => r.Worlds.Any( w => w.WorldName.FullName == newWorld.FullName ) );
                            if( alreadyDefiner != null )
                            {
                                m.Warn( $"World '{newWorld.FullName}' is already defined in repository {alreadyDefiner.OriginUrl}. It is skipped." );
                            }
                            else
                            {
                                m.Info( $"Found a new World definition: creating '{newWorld.FullName}' entry." );
                                newWorld.HasDefinitionFile = true;
                                _worlds.Add( new WorldInfo( this, newWorld ) );
                                hasChanged = true;
                            }
                        }
                        // Finally: ensures that the $Local/FullName directory exists.
                        foreach( var w in _worlds )
                        {
                            Directory.CreateDirectory( localDir.AppendPart( w.WorldName.FullName ) );
                        }
                    }
                }
                return (IsOpen,hasChanged);
            }

            bool DoOpen( IActivityMonitor m, out bool opened, out NormalizedPath localDir )
            {
                opened = false;
                localDir = Root.AppendPart( "$Local" );
                if( _git == null )
                {
                    _git = SimpleGitRepository.Ensure( m, this, Root, Root.LastPart, BranchName, checkOutBranchName: true );
                    if( _git == null ) return false;
                    // Ensures that the $Local directory is created and that the .gitignore ignores it.
                    // The .gitignore file is created only once.
                    Directory.CreateDirectory( localDir );
                    var ignore = Root.AppendPart( ".gitignore" );
                    if( !File.Exists( ignore ) ) File.WriteAllText( ignore, "$Local/" + Environment.NewLine );
                    opened = true;
                }
                return true;
            }

            /// <summary>
            /// Gets all the stacks that this repository defines (or should define: see <see cref="WorldInfo.HasDefinitionFile"/>).
            /// </summary>
            public IReadOnlyList<WorldInfo> Worlds => _worlds;

            public void Dispose()
            {
                if( _git != null )
                {
                    _git.Dispose();
                    _git = null;
                }
            }

            /// <summary>
            /// This is public to be used by integrations tests.
            /// </summary>
            /// <param name="path">The original path.</param>
            /// <returns>The cleaned up path.</returns>
            public static string CleanPathDirName( string path )
            {
                var p = path.Replace( ".git", "" )
                            .Replace( "_git", "" )
                            .Replace( '/', '_' )
                            .Replace( ':', '_' )
                            .Replace( "__", "_" )
                            .Trim( '_' )
                            .ToLowerInvariant();
                if( p.Length > 50 )
                {
                    p = p.Replace( "-stack", "" );
                }
                if( p.Length > 50 )
                {
                    p = SHA1Value.ComputeHash( p ).ToString();
                }
                return p;
            }
        }
    }

}
