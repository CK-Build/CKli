using CK.Core;
using CK.SimpleKeyVault;
using LibGit2Sharp;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace CK.Env
{

    /// <summary>
    /// Root folder of a stack contains a <see cref="StackRepository"/>
    /// and a current <see cref="World"/> that can be the default one or a parallel world.
    /// </summary>
    public sealed partial class StackRoot : IDisposable
    {
        readonly ICkliApplicationContext _appContext;
        readonly StackRepository _stackRepository;
        readonly WorldBearer _bearer;

        StackRoot( ICkliApplicationContext appContext, StackRepository stackRepository, WorldBearer bearer )
        {
            _appContext = appContext;
            _stackRepository = stackRepository;
            _bearer = bearer;
        }

        /// <summary>
        /// Gets the root path of the stack.
        /// </summary>
        public NormalizedPath RootPath => _stackRepository.StackRoot;

        /// <summary>
        /// Gets the name of this stack that is necessarily the last part of the <see cref="StackRoot"/>.
        /// </summary>
        public string StackName => _stackRepository.StackName;

        /// <summary>
        /// Gets whether this stack is public.
        /// </summary>
        public bool IsPublic => _stackRepository.IsPublic;

        /// <summary>
        /// Gets the Git stack repository.
        /// </summary>
        public StackRepository StackRepository => _stackRepository;

        /// <summary>
        /// Gets the currently opened world.
        /// </summary>
        public World? World => _bearer.World;

        /// <summary>
        /// Tries to open a different (case insensitive) world, closing the current <see cref="World"/> if it is opened.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="name">The world name to open. Must be a world from this <see cref="StackRepository"/>.</param>
        /// <returns>True on success, false on error.</returns>
        public bool OpenWorld( IActivityMonitor monitor, IWorldName name, bool checkWorldLayout )
        {
            if( string.Equals( _bearer.World?.WorldName.FullName, name.FullName, StringComparison.OrdinalIgnoreCase ) )
            {
                return true;
            }
            var worlds = _stackRepository.TryRefreshWorlds( monitor, true );
            if( worlds == null ) return false;
            var w = worlds.FirstOrDefault( w => string.Equals( w.ParallelName, name.ParallelName, StringComparison.OrdinalIgnoreCase ) );
            if( w == null )
            {
                monitor.Error( $"Unable to find World '{name}' definition in '{_stackRepository.Path}'." );
                return false;
            }
            // First close the world to release any opened repository handle.
            _bearer.Close( monitor );
            if( checkWorldLayout )
            {
                var check = CheckWorldLayout( monitor, w, true );
                if( !check.Success ) return false;
            }
            return _bearer.OpenWorld( monitor, w, _stackRepository._worldStore );
        }

        /// <summary>
        /// Closes the <see cref="World"/> if it's opened.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        public void CloseWorld( IActivityMonitor monitor ) => _bearer.Close( monitor );

        public (bool Success, bool RequiresFix) CheckWorldLayout( IActivityMonitor monitor, LocalWorldName world, bool applyAutoFix )
        {
            Throw.CheckArgument( world?.Name == StackName );
            var def = WorldDefinitionFile.Load( monitor, _stackRepository._worldStore.ReadWorldDescription( monitor, world ) );
            var definitionlayout = def?.ReadLayout( monitor );
            if( definitionlayout == null ) return (false, false);

            var currentLayout = world.ParallelName == null
                                    ? GitWorkingFolderLayout.Create( monitor, world.Root, ignore: p => p.LastPart.EndsWith( ']' ) || _stackRepository.Path.EndsWith( p ) )
                                    : GitWorkingFolderLayout.Create( monitor, world.Root );
            if( currentLayout == null ) return (false, false);

            if( currentLayout.MissingRoot )
            {
                bool success = GitRepositoryBase.EnsureWorkingFolders( monitor, _appContext.KeyStore, world.Root, definitionlayout, IsPublic, IWorldName.DevelopName );
                return (success, false);
            }
            bool hasBlockingIssues = false;
            if( currentLayout.HasIssues )
            {
                hasBlockingIssues = HandleCurrentLayoutIssues( monitor, currentLayout );
                if( !hasBlockingIssues )
                {
                    var diff = currentLayout.CreateDiff( definitionlayout );
                    foreach( var d in diff.Deleted )
                    {
                        monitor.Error( $"Folder '{d}' is not a repository of this stack. It should be deleted." );
                        hasBlockingIssues = true;
                    }
                    if( diff.HasAutomaticFixes && applyAutoFix )
                    {
                        hasBlockingIssues |= !ApplyAddedRemovedAndRemoteMovedFixes( monitor, diff );
                    }
                }
            }
            return (true, hasBlockingIssues);

            static bool HandleCurrentLayoutIssues( IActivityMonitor monitor, GitWorkingFolderLayout currentLayout )
            {
                bool hasBlockingIssues = false;
                foreach( var i in currentLayout.RepositoryIssues )
                {
                    monitor.Error( $"Unable to read Git repository '{i}/.git'. It may be corrupted: this requires a manual fix." );
                    hasBlockingIssues = true;
                }
                HashSet<string>? issueByLastPart = null;
                foreach( var i in currentLayout.HomonymIssues )
                {
                    monitor.Error( $"Multiple Git folder named '{i.Key}' exist: {i.Select( p => p.ToString() ).Concatenate()}. This requires a manual fix." );
                    issueByLastPart ??= new HashSet<string>();
                    issueByLastPart.AddRange( i.Select( i => i.LastPart ) );
                    hasBlockingIssues = true;
                }
                foreach( var i in currentLayout.OriginUrlIssues )
                {
                    monitor.Error( $"Remote '{i.Key}' has multiple clones: {i.Select( p => p.ToString() ).Concatenate()}. A repository must be cloned only once per stack. This requires a manual fix." );
                    issueByLastPart ??= new HashSet<string>();
                    issueByLastPart.AddRange( i.Select( i => i.LastPart ) );
                    hasBlockingIssues = true;
                }
                foreach( var i in currentLayout.Layout.Select( l => (l.SubPath, l.OriginUrl, FromRepo: l.OriginUrl.Segments[^1]) )
                                                      .Where( l => l.SubPath.LastPart != l.FromRepo ) )
                {
                    var autoMoveTarget = currentLayout.Root.Combine( i.SubPath.RemoveLastPart().AppendPart( i.FromRepo ) );
                    bool canBeFixed = !Directory.Exists( autoMoveTarget ) && !File.Exists( autoMoveTarget );
                    canBeFixed &= issueByLastPart == null
                                      || (!issueByLastPart.Contains( i.FromRepo )) && !issueByLastPart.Contains( i.SubPath.LastPart );
                    if( canBeFixed )
                    {
                        using( monitor.OpenWarn( $"Folder '{i.SubPath}' must be renamed to '{i.FromRepo}' since repository Url is '{i.OriginUrl}'." ) )
                        {
                            try
                            {
                                Directory.Move( currentLayout.Root.Combine( i.SubPath ), autoMoveTarget );
                                monitor.CloseGroup( "Success." );
                            }
                            catch( Exception ex )
                            {
                                monitor.Error( $"While trying to rename folder '{i.SubPath}'.", ex );
                                hasBlockingIssues = true;
                            }
                        }
                    }
                    else
                    {
                        monitor.Error( $"Folder '{i.SubPath}': '{i.SubPath.LastPart}' should be '{i.FromRepo}' since repository Url is '{i.OriginUrl}'. This requires a manual fix." );
                        hasBlockingIssues = true;
                    }
                }

                return hasBlockingIssues;
            }

            bool ApplyAddedRemovedAndRemoteMovedFixes( IActivityMonitor monitor, GitWorkingFolderLayout.Diff diff )
            {
                bool success = true;
                foreach( var a in diff.Added )
                {
                    using( monitor.OpenInfo( $"Cloning missing folder '{a.SubPath}' from '{a.OriginUrl}'." ) )
                    {
                        success &= GitRepositoryBase.EnsureWorkingFolder( monitor,
                                                                      _appContext.KeyStore,
                                                                      diff.Local.Root.Combine( a.SubPath ),
                                                                      a.OriginUrl,
                                                                      IsPublic,
                                                                      IWorldName.DevelopName );
                    }
                }
                foreach( var m in diff.Moved )
                {
                    using( monitor.OpenInfo( $"Moving folder '{m.Current}' to '{m.Target}'." ) )
                    {
                        try
                        {
                            Directory.Move( diff.Local.Root.Combine( m.Current ), diff.Local.Root.Combine( m.Target ) );
                            monitor.CloseGroup( "Success." );
                        }
                        catch( Exception ex )
                        {
                            monitor.Error( $"While trying to move folder '{m.Current}' to '{m.Target}'. This must be fixed manually.", ex );
                            success = false;
                        }
                    }
                }
                foreach( var r in diff.RemoteMoved )
                {
                    var folder = diff.Local.Root.Combine( r.SubPath );
                    // Skip missing directory (Move has failed).
                    if( !Directory.Exists( folder ) ) continue;

                    using( monitor.OpenInfo( $"Updating the remote origin of folder '{r.SubPath}' to '{r.OriginUrl}'." ) )
                    {
                        try
                        {
                            Repository g = new Repository( folder );
                            var remotes = g.Network.Remotes;
                            remotes.Update( "origin", u => u.Url = r.OriginUrl.ToString() );
                            g.Dispose();
                            monitor.CloseGroup( "Success." );
                        }
                        catch( Exception ex )
                        {
                            monitor.Error( $"While trying to set remote origin for '{r.SubPath}' to '{r.OriginUrl}'.", ex );
                            success = false;
                        }
                    }
                }

                return success;
            }
        }

        /// <summary>
        /// Disposes the <see cref="StackRepository"/> and closes the world if it is opened.
        /// </summary>
        public void Dispose()
        {
            _bearer.Dispose();
            _stackRepository.Dispose();
        }

        /// <summary>
        /// Tries to load a <see cref="StackRoot"/> with an optionally opened <see cref="StackRoot.World"/> from a path.
        /// Caution: The stack is null if the <paramref name="path"/> is not a path stack or below.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="appContext">The application context.</param>
        /// <param name="path">The path to load.</param>
        /// <param name="stack">A non null stack object if the <paramref name="path"/> is in a stack.</param>
        /// <param name="loadPathWorld">False to not open the world even if the path is inside a world.</param>
        /// <returns>True on success, false on error.</returns>
        public static bool TryLoad( IActivityMonitor monitor,
                                    ICkliApplicationContext appContext,
                                    NormalizedPath path,
                                    out StackRoot? stack,
                                    bool loadPathWorld = true )
        {
            stack = null;
            if( !StackRepository.TryOpenFrom( monitor, appContext.KeyStore, path, out var gitStack ) ) return false;
            if( gitStack == null ) return true;

            int firstSubIdx = gitStack.StackRoot.Parts.Count;
            if( loadPathWorld && path.Parts.Count >= firstSubIdx )
            {
                if( path.Parts.Count > firstSubIdx )
                {
                    var first = path.Parts[firstSubIdx];
                    if( first[0] == '[' && first[^1] == ']' )
                    {
                        var parallel = gitStack.WorldDefinitions.FirstOrDefault( w => w.ParallelName != null && first.AsSpan( 1, first.Length - 2 ).Equals( w.ParallelName ) );
                        if( parallel == null )
                        {
                            monitor.Warn( $"Folder '{first}' in '{gitStack.StackRoot}' looks like a Parallel world folder but no such parallel world exists. It should be deleted." );
                        }
                        else
                        {
                            return TryLoadWithWorld( monitor, appContext, gitStack, parallel, path, out stack );
                        }
                    }
                }
                var defaultWorld = gitStack.GetDefaultWorld( monitor );
                if( defaultWorld == null ) return false;
                return TryLoadWithWorld( monitor, appContext, gitStack, defaultWorld, path, out stack );
            }

            stack = new StackRoot( appContext, gitStack, new WorldBearer( appContext ) );
            return true;

            static bool TryLoadWithWorld( IActivityMonitor monitor, ICkliApplicationContext appContext, StackRepository gitStack, LocalWorldName world, NormalizedPath path, out StackRoot? stack )
            {
                stack = null;
                var b = new WorldBearer( appContext );
                if( !b.OpenWorld( monitor, world, gitStack._worldStore ) ) return false;
                stack = new StackRoot( appContext, gitStack, b );
                return true;
            }
        }

        /// <summary>
        /// Tries to create a <see cref="StackRoot"/> from the stack remote repository by cloning the
        /// stack repository (in ".PrivateStack" or ".PublicStack") and cloning the default world repositories.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="appContext">The application context.</param>
        /// <param name="gitStackUri">The stack repository.</param>
        /// <param name="path">The path where the Stack folder will be created.</param>
        /// <param name="isPublic">Whether the repository is a public or private one.</param>
        /// <param name="allowDuplicate">Allows a <paramref name="gitStackUri"/> that already exists in <see cref="StackRootRegistry"/> to be cloned.</param>
        /// <param name="openDefaultWorld">tries to set the <see cref="World"/> on the default one on success.</param>
        /// <returns>A non null stack object on success. The default world may be null if opening it was impossible.</returns>
        public static StackRoot? Create( IActivityMonitor monitor,
                                         ICkliApplicationContext appContext,
                                         Uri gitStackUri,
                                         NormalizedPath path,
                                         bool isPublic,
                                         bool allowDuplicate = false,
                                         bool openDefaultWorld = true )
        {
            Throw.CheckNotNullArgument( monitor );
            Throw.CheckNotNullArgument( appContext );
            Throw.CheckNotNullArgument( gitStackUri );
            if( !gitStackUri.IsAbsoluteUri )
            {
                monitor.Error( $"'{gitStackUri}' must be an absolute url to a 'XX-Stack' git repository." );
                return null;
            }
            gitStackUri = GitRepositoryKey.CheckAndNormalizeRepositoryUrl( gitStackUri );
            var uriLeaf = gitStackUri.Segments[^1];
            if( !Regex.IsMatch( uriLeaf, "\\w\\w-Stack$" ) )
            {
                monitor.Error( $"'{gitStackUri}' must be a valid url to a 'XX-Stack' git repository." );
                return null;
            }
            var registry = StackRootRegistry.Load( monitor, appContext.UserHostPath );
            var alreadyCloned = registry.KnownStacks.FirstOrDefault( s => s.StackUrl == gitStackUri );
            if( alreadyCloned != null )
            {
                var msg = $"This stack '{uriLeaf}' is already cloned at '{alreadyCloned.RootPath}'";
                if( !allowDuplicate )
                {
                    monitor.Error( msg );
                    return null;
                }
                monitor.Info( msg );
            }
            if( uriLeaf.Contains( '[' ) || uriLeaf.Contains( ']' ) )
            {
                monitor.Error( $"Invalid repository name: '{uriLeaf}' must not contain '[' or ']'." );
                return null;
            }
            var stackName = uriLeaf.Substring( 0, uriLeaf.Length - 6 );
            var aboveStackRoot = new NormalizedPath( Environment.CurrentDirectory )
                                   .Combine( path )
                                   .ResolveDots( throwOnAboveRoot: false );
            var stackRoot = aboveStackRoot.AppendPart( stackName );
            if( stackRoot.Parts.Count <= 2 )
            {
                monitor.Error( $"Resolved cloned path '{stackRoot}' is too short." );
                return null;
            }
            if( Directory.Exists( stackRoot ) || File.Exists( stackRoot ) )
            {
                monitor.Error( $"Resolved cloned path '{stackRoot}' already exists." );
                return null;
            }
            var parentStack = StackRepository.FindGitStackPath( aboveStackRoot );
            if( !parentStack.IsEmptyPath )
            {
                var stackAbove = parentStack.RemoveLastPart();
                var safeRoot = stackAbove.RemoveLastPart().AppendPart( stackName );
                if( Directory.Exists( safeRoot ) || File.Exists( safeRoot ) )
                {
                    monitor.Error( $"Resolved stack path '{stackRoot}' is inside stack '{stackAbove}': moving it to '{safeRoot}' but this path already exists." );
                    return null;
                }
                monitor.Warn( $"Resolved stack path '{stackRoot}' is inside stack '{stackAbove}': moving it to {safeRoot}." );
                stackRoot = safeRoot;
            }
            monitor.Info( $"Creating stack root folder '{stackRoot}'." );
            Directory.CreateDirectory( stackRoot );
            var gitStack = StackRepository.Ensure( monitor, appContext.KeyStore, gitStackUri, isPublic, aboveStackRoot );
            if( gitStack == null )
            {
                DeleteUselessFolder( monitor, stackRoot );
                return null;
            }
            var stack = new StackRoot( appContext, gitStack, new WorldBearer( appContext ) );
            var defName = gitStack.GetDefaultWorld( monitor );
            var def = defName != null
                        ? WorldDefinitionFile.Load( monitor, gitStack._worldStore.ReadWorldDescription( monitor, defName ) )
                        : null;
            var layout = def?.ReadLayout( monitor );
            if( layout == null )
            {
                gitStack.Dispose();
                return null;
            }
            if( openDefaultWorld )
            {
                Debug.Assert( defName != null );
                // Ignores errors while cloning the repositories.
                // Tries to open the world. if it fails, the stack.World will be null.
                GitRepositoryBase.EnsureWorkingFolders( monitor, appContext.KeyStore, stackRoot, layout, isPublic, IWorldName.DevelopName );
                stack.OpenWorld( monitor, defName, false );
            }
            registry.OnCreated( stack );
            return stack;

            static void DeleteUselessFolder( IActivityMonitor monitor, NormalizedPath stackRoot )
            {
                monitor.Info( $"Deleting stack root folder." );
                try
                {
                    Directory.Delete( stackRoot, true );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"While deleting '{stackRoot}'.", ex );
                }
            }
        }
    }
}
