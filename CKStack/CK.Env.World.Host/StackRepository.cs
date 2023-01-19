using CK.Core;
using CK.SimpleKeyVault;
using LibGit2Sharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Env.GitWorldStore;

namespace CK.Env
{

    /// <summary>
    /// Encapsulate the <see cref="SimpleGitRepository"/> with its worlds.
    /// </summary>
    public sealed class StackRepository : IDisposable
    {
        public const string PublicStackName = ".PublicStack";
        public const string PrivateStackName = ".PrivateStack";

        readonly SimpleGitRepository _git;
        readonly NormalizedPath _stackRoot;
        LocalWorldName[] _worldsDef;
        internal readonly IWorldStore _worldStore;
        bool _isDirty;

        /// <summary>
        /// Gets the root path of the stack.
        /// </summary>
        public NormalizedPath StackRoot => _stackRoot;

        /// <summary>
        /// Gets the path of the ".PrivateStack" or ".PublicStack". 
        /// </summary>
        public NormalizedPath Path => _git.FullPhysicalPath;

        /// <summary>
        /// Gets the name of this stack that is necessarily the last part of the <see cref="StackRoot"/>.
        /// </summary>
        public string StackName => StackRoot.LastPart;

        /// <summary>
        /// Gets the branch name: Should always be <see cref="IWorldName.MasterName"/> but this may be changed.
        /// </summary>
        public string BranchName => _git.CurrentBranchName;

        /// <summary>
        /// Gets whether this stack is dirty.
        /// </summary>
        public bool IsDirty => _isDirty;

        /// <summary>
        /// Gets whether this stack is public.
        /// </summary>
        public bool IsPublic => _git.IsPublic;

        /// <summary>
        /// Gets whether the stack's repository url.
        /// </summary>
        public Uri OriginUrl => _git.OriginUrl;

        /// <summary>
        /// Gets all the worlds that this stack contains.
        /// Use <see cref="TryRefreshWorlds(IActivityMonitor, bool)"/> to update them.
        /// </summary>
        public IReadOnlyList<LocalWorldName> WorldDefinitions => _worldsDef;

        /// <summary>
        /// Gets the default world if it exists or emits an error if it doesn't.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>The default world if it exists.</returns>
        public LocalWorldName? GetDefaultWorld( IActivityMonitor monitor )
        {
            var defaultWorld = _worldsDef.FirstOrDefault( w => w.ParallelName == null );
            if( defaultWorld == null )
            {
                monitor.Error( $"Stack '{StackRoot}': the default World definition is missing. Expecting file '{_git.FullPhysicalPath}/{StackName}.World.xml'." );
            }
            return defaultWorld;
        }

        /// <summary>
        /// Ensures that the Git Repository of this stack is opened and updates the <see cref="WorldDefinitions"/> from the files.
        /// Returns true on success, false on error.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="pull">True to pull and refresh the <see cref="WorldDefinitions"/> list even if <see cref="IsOpen"/> is already true.</param>
        /// <returns>The non null list of worlds or null on error.</returns>
        public IReadOnlyList<LocalWorldName>? TryRefreshWorlds( IActivityMonitor monitor, bool pull )
        {
            if( pull )
            {
                var r = _git.Pull( monitor, MergeFileFavor.Theirs );
                if( !r.Success ) return null;
                if( !r.ReloadNeeded ) return _worldsDef;
            }
            return _worldsDef = ReadWorlds( StackRoot );
        }

        /// <summary>
        /// Commits and push changes to the remote.
        /// Nothing is done if <see cref="IsDirty"/> is false.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        internal bool PushChanges( IActivityMonitor m )
        {
            if( !_isDirty ) return false;
            CommittingResult result = _git.Commit( m, "Automatic synchronization commit." );
            if( result == CommittingResult.NoChanges )
            {
                m.Trace( "Nothing committed. Skipping push." );
                _isDirty = false;
            }
            else
            {
                _isDirty = result == CommittingResult.Error || !_git.Push( m );
            }
            return !_isDirty;
        }

        sealed class WorldStore : IWorldStore
        {
            readonly StackRepository _repository;

            public WorldStore( StackRepository repository )
            {
                _repository = repository;
            }

            public bool IsSingleWorld => false;

            public IRootedWorldName? CreateNewParrallel( IActivityMonitor m, IRootedWorldName source, string parallelName, XDocument content )
            {
                Throw.CheckNotNullArgument( source );
                Throw.CheckArgument( content?.Root != null );
                Throw.CheckNotNullOrWhiteSpaceArgument( parallelName );

                CkeckWorldStackName( source );

                var newRoot = _repository.StackRoot.AppendPart( $"[{parallelName}]" );
                var newDesc = _repository._git.FullPhysicalPath.AppendPart( $"{_repository.StackName}[{parallelName}].World.xml" );
                var newOne = new LocalWorldName( _repository.StackName, parallelName, newRoot, newDesc );

                if( File.Exists( newOne.XmlDescriptionFilePath ) )
                {
                    m.Error( $"Unable to create '{newOne}' world: file {newOne.XmlDescriptionFilePath} already exists." );
                    return null;
                }
                if( Directory.Exists( newOne.Root ) )
                {
                    m.Error( $"Unable to create '{newOne}' world: directory {newOne.Root} already exists." );
                    return null;
                }
                content.Save( newOne.XmlDescriptionFilePath );
                Directory.CreateDirectory( newOne.Root );
                return newOne;
            }

            LocalWorldName ToLocal( IWorldName world )
            {
                CkeckWorldStackName( world );
                if( world is not LocalWorldName loc )
                {
                    var newRoot = world.ParallelName == null ? _repository.StackRoot : _repository.StackRoot.AppendPart( $"[{world.ParallelName}]" );
                    var newDesc = _repository._git.FullPhysicalPath.AppendPart( $"{world.FullName}.World.xml" );
                    loc = new LocalWorldName( world.Name, world.ParallelName, newRoot, newDesc );
                }
                return loc;
            }

            void CkeckWorldStackName( IWorldName world )
            {
                if( world.Name != _repository.StackName )
                {
                    Throw.ArgumentException( $"World '{world.FullName}' doesn't belong to the '{_repository.StackName}' stack." );
                }
            }

            NormalizedPath ToLocalStateFilePath( IWorldName w ) => GetWorkingLocalFolder( w ).AppendPart( "LocalState.xml" );

            NormalizedPath ToSharedStateFilePath( IWorldName w )
            {
                var local = ToLocal( w );
                var def = local.XmlDescriptionFilePath;
                Debug.Assert( def.EndsWith( ".World.xml" ) );
                return def.RemoveLastPart().AppendPart( def.LastPart.Substring( 0, def.LastPart.Length - 3 ) + "SharedState.xml" );
            }

            public LocalWorldState GetOrCreateLocalState( IActivityMonitor m, IWorldName w )
            {
                var p = ToLocalStateFilePath( w );
                if( File.Exists( p ) ) return new LocalWorldState( this, w, XDocument.Load( p, LoadOptions.SetLineInfo ) );
                m.Info( $"Creating new local state for {w.FullName}." );
                return new LocalWorldState( this, w );
            }

            public SharedWorldState GetOrCreateSharedState( IActivityMonitor m, IWorldName w )
            {
                var p = ToSharedStateFilePath( w );
                if( File.Exists( p ) ) return new SharedWorldState( this, w, XDocument.Load( p, LoadOptions.SetLineInfo ) );
                m.Info( $"Creating new shared state for {w.FullName}." );
                return new SharedWorldState( this, w );
            }

            public NormalizedPath GetWorkingLocalFolder( IWorldName w )
            {
                var local = ToLocal( w );
                var def = local.XmlDescriptionFilePath;
                Debug.Assert( def.EndsWith( ".World.xml" ) );
                return def.RemoveLastPart().AppendPart( "$Local" ).AppendPart( w.FullName );
            }

            public XDocument ReadWorldDescription( IActivityMonitor monitor, IWorldName w )
            {
                var local = ToLocal( w );
                var d = XDocument.Load( local.XmlDescriptionFilePath, LoadOptions.SetLineInfo );
                var expectedRootName = w.ParallelName == null ? $"{w.Name}-World" : $"{w.Name}-{w.ParallelName}.World";
                if( d.Root == null || d.Root.Name.LocalName != expectedRootName )
                {
                    Throw.InvalidDataException( $"Invalid Xml document '{local.XmlDescriptionFilePath}'. Root element named '<{expectedRootName}>'." );
                }
                return d;
            }

            public bool SaveLocalState( IActivityMonitor m, IWorldName w, XDocument d )
            {
                var p = ToLocalStateFilePath( w );
                d.Save( p );
                return true;
            }

            public bool SaveSharedState( IActivityMonitor m, IWorldName w, XDocument d )
            {
                var p = ToSharedStateFilePath( w );
                d.Save( p );
                _repository._isDirty = true;
                return true;
            }
        }

        StackRepository( SimpleGitRepository git, in NormalizedPath stackRoot, LocalWorldName[] worlds )
        {
            _git = git;
            _stackRoot = stackRoot;
            _worldStore = new WorldStore( this );
            _worldsDef = worlds;
        }

        /// <summary>
        /// Tries to open a stack directory from a path.
        /// This lookups the ".PrivateStack" or ".PublicStack" in and above <paramref name="path"/>: if none
        /// are found, this is successful but <paramref name="stackRepository"/> is null.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="keyStore">The secret key store.</param>
        /// <param name="path">The starting path.</param>
        /// <param name="stackRepository">The resulting stack directory if found and opened successfully.</param>
        /// <param name="branchName">Defaults to <see cref="IWorldName.MasterName"/>.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool TryOpenFrom( IActivityMonitor monitor,
                                        SecretKeyStore keyStore,
                                        in NormalizedPath path,
                                        out StackRepository? stackRepository,
                                        string? branchName = null )
        {
            stackRepository = null;
            var gitPath = FindGitStackPath( path );
            if( gitPath.IsEmptyPath ) return true;

            var isPublic = gitPath.LastPart == PublicStackName;
            var git = SimpleGitRepository.Open( monitor,
                                                keyStore,
                                                gitPath,
                                                gitPath,
                                                isPublic,
                                                branchName ?? IWorldName.MasterName,
                                                checkOutBranchName: true );
            if( git != null )
            {
                var stackRoot = gitPath.RemoveLastPart();
                if( !HasOriginUrlStackSuffix( monitor, git.OriginUrl ) )
                {
                    monitor.Error( $"The repository Url '{git.OriginUrl}' must have '-Stack' suffix." );
                    return false;
                }
                var nStack = git.OriginUrl.Segments[^1];
                if( !nStack.StartsWith( stackRoot.LastPart ) || stackRoot.LastPart.Length > nStack.Length - 6 )
                {
                    monitor.Error( $"Stack folder '{stackRoot.LastPart}' must be '{nStack.Remove( nStack.Length - 6 )}' since repository Url is '{git.OriginUrl}'." );
                    return false;
                }
                stackRepository = new StackRepository( git, stackRoot, ReadWorlds( gitPath ) );
                return true;
            }
            return false;

        }

        /// <summary>
        /// Finds a git stack ".PrivateStack" or ".PublicStack" above. 
        /// </summary>
        /// <param name="path">Starting path.</param>
        /// <returns>A git stack or the empty path.</returns>
        public static NormalizedPath FindGitStackPath( NormalizedPath path )
        {
            foreach( var tryPath in path.PathsToFirstPart( null, new[] { PublicStackName, PrivateStackName } ) )
            {
                if( Directory.Exists( tryPath ) ) return tryPath;
            }
            return default;
        }

        /// <summary>
        /// Ensures that a root stack directory exists. The ".PrivateStack" or ".PublicStack" is checked out if needed.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="keyStore">The secret key store.</param>
        /// <param name="url">The url of the remote.</param>
        /// <param name="isPublic">Whether this repository is public.</param>
        /// <param name="aboveStackRoot">The path where the root stack folder will be created. Must not be empty.</param>
        /// <param name="branchName">Defaults to <see cref="IWorldName.MasterName"/>.</param>
        /// <returns>The repository or null on error.</returns>
        public static StackRepository? Ensure( IActivityMonitor monitor,
                                               SecretKeyStore keyStore,
                                               Uri url,
                                               bool isPublic,
                                               in NormalizedPath aboveStackRoot,
                                               string? branchName = null )
        {
            Throw.CheckNotNullArgument( monitor );
            Throw.CheckNotNullArgument( keyStore );
            Throw.CheckNotNullArgument( url );
            Throw.CheckArgument( !aboveStackRoot.IsEmptyPath && aboveStackRoot.LastPart != PublicStackName && aboveStackRoot.LastPart != PrivateStackName );

            if( !HasOriginUrlStackSuffix( monitor, url ) )
            {
                monitor.Error( $"The repository Url '{url}' must have '-Stack' suffix." );
                return null;
            }
            var rootName = url.Segments[^1];
            var stackRoot = aboveStackRoot.AppendPart( rootName.Remove( rootName.Length - 6 ) );

            branchName ??= IWorldName.MasterName;
            NormalizedPath gitPath = GetGitStackPath( stackRoot, isPublic );

            var git = SimpleGitRepository.Ensure( monitor,
                                                  new GitRepositoryKey( keyStore, url, isPublic ),
                                                  gitPath,
                                                  gitPath,
                                                  branchName,
                                                  checkOutBranchName: true );
            if( git != null )
            {
                // Ensures that the $Local directory is created.
                var localDir = gitPath.AppendPart( "$Local" );
                if( !Directory.Exists( localDir ) )
                {
                    // The .gitignore ignores it. It is created only once.
                    Directory.CreateDirectory( localDir );
                    var ignore = gitPath.AppendPart( ".gitignore" );
                    if( !File.Exists( ignore ) ) File.WriteAllText( ignore, "$Local/" + Environment.NewLine );
                }
                return new StackRepository( git, stackRoot, ReadWorlds( gitPath ) );
            }
            return null;
        }

        static bool HasOriginUrlStackSuffix( IActivityMonitor monitor, Uri stackOriginUrl )
        {
            var n = stackOriginUrl.Segments[^1];
            return n.EndsWith( "-Stack" ) && n.Length >= 8;
        }

        internal static NormalizedPath GetGitStackPath( NormalizedPath stackRoot, bool isPublic )
        {
            return stackRoot.AppendPart( isPublic ? PublicStackName : PrivateStackName );
        }

        internal static LocalWorldName[] ReadWorlds( NormalizedPath gitPath )
        {
            return Directory.GetFiles( gitPath, $"{gitPath.Parts[gitPath.Parts.Count-2]}*.World.xml" )
                        .Select( p => LocalWorldName.TryParseDefinitionFilePath( p ) )
                        .Where( w => w != null )
                        .ToArray()!;
        }

        public void Dispose()
        {
            _git.Dispose();
        }
    }

}
