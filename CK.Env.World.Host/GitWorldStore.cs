using CK.Core;
using CK.SimpleKeyVault;
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
    /// Implements a multiple or single World store based on Git repositories: the <see cref="StackRepositories"/>
    /// are the repositories that contain the <see cref="WorldInfo"/>.
    /// </summary>
    public sealed partial class GitWorldStore : WorldStore, ICommandMethodsProvider, IDisposable
    {
        readonly NormalizedPath _rootStorePath;
        readonly List<StackRepo> _stackRepos;

        /// <summary>
        /// Initializes a new multiple world store.
        /// </summary>
        /// <param name="rootStorePath">The root path where the stores repositories and local states are hosted.</param>
        /// <param name="mapping">The world mapping.</param>
        /// <param name="keyStore">The key store to use.</param>
        /// <param name="commandRegister">Optional command register.</param>
        public GitWorldStore(
            NormalizedPath rootStorePath,
            IWorldLocalMapping mapping,
            SecretKeyStore keyStore,
            CommandRegister? commandRegister )
            : base( mapping )
        {
            _rootStorePath = rootStorePath;
            SecretKeyStore = keyStore;
            StacksFilePath = rootStorePath.AppendPart( "Stacks.xml" );
            _stackRepos = new List<StackRepo>();
            mapping.MappingChanged += Mapping_MappingChanged;
            commandRegister?.Register( this );
        }

        /// <summary>
        /// Initializes a new single world store.
        /// A <see cref="WorldStore.SingleWorldMapping"/> mapping is created.
        /// </summary>
        /// <param name="rootStorePath">The root path where the stores repositories and local states are hosted.</param>
        /// <param name="singleWorld">The single world to host.</param>
        /// <param name="worldRootPath">The world root folder (where the solutions are checked out).</param>
        /// <param name="keyStore">The key store to use.</param>
        public GitWorldStore( NormalizedPath rootStorePath,
                              IRootedWorldName singleWorld,
                              SecretKeyStore keyStore )
            : base( singleWorld )
        {
            _rootStorePath = rootStorePath;
            SecretKeyStore = keyStore;
            StacksFilePath = rootStorePath.AppendPart( "Stacks.xml" );
            _stackRepos = new List<StackRepo>();
        }

        void Mapping_MappingChanged( object? sender, EventArgs e )
        {
            foreach( var r in _stackRepos )
            {
                foreach( var w in r.Worlds )
                {
                    w.WorldName.UpdateRoot( WorldLocalMapping );
                }
            }
        }

        /// <summary>
        /// Gets the root path of the store: it contains the <see cref="StackRepo"/>'s git working folder.
        /// </summary>
        public NormalizedPath RootPath => _rootStorePath;

        NormalizedPath ICommandMethodsProvider.CommandProviderName => MultipleWorldHome.WorldCommandPath;

        SecretKeyStore SecretKeyStore { get; }

        NormalizedPath StacksFilePath { get; }

        /// <summary>
        /// Gets the stacks repositories.
        /// </summary>
        public IReadOnlyList<StackRepo> StackRepositories => _stackRepos;

        /// <summary>
        /// Reads the Stacks.xml file and instantiates the <see cref="StackRepo"/> objects and
        /// their <see cref="WorldInfo"/>: creating the StackRepo declares the required secrets
        /// in the key store.
        /// <para>
        /// In multiple words mode, when there is no stack or if an error prevented the file to be read,
        /// the CK and CK-Build public stacks are automatically created.
        /// </para>
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <returns>Always true in multiple worlds mode. False if the single world failed to be read.</returns>
        public bool ReadStacksFromLocalStacksFilePath( IActivityMonitor monitor )
        {
            if( !File.Exists( StacksFilePath ) )
            {
                monitor.Warn( $"File '{StacksFilePath}' not found." );
                if( SingleWorld != null ) return false;
            }
            else
            {
                using( monitor.OpenInfo( $"Reading '{StacksFilePath}'." ) )
                {
                    try
                    {
                        IEnumerable<StackRepo> all;
                        var root = XDocument.Load( StacksFilePath ).Root;
                        LocalWorldName? localName = null;
                        if( SingleWorld == null )
                        {
                            all = root.Elements().Select( e => StackRepo.Parse( this, e ) );
                        }
                        else
                        {
                            var wE = root.Elements().Elements( "Worlds" ).Elements().FirstOrDefault( e => (string?)e.Attribute( "FullName" ) == SingleWorld.WorldName.FullName );
                            if( wE == null )
                            {
                                monitor.Error( $"Unable to find WorldfInfo element with FullName attribute '{SingleWorld.WorldName.FullName}'." );
                                return false;
                            }
                            var r = StackRepo.Parse( this, wE.Parent.Parent, wE );
                            localName = r.Worlds[0].WorldName;
                            all = new[] { r };
                        }
                        _stackRepos.Clear();
                        _stackRepos.AddRange( all );
                        if( localName != null )
                        {
                            Debug.Assert( SingleWorld != null );
                            SingleWorld.UpdateSingleName( localName );
                            return true;
                        }
                    }
                    catch( Exception ex )
                    {
                        monitor.Error( $"Unable to read '{StacksFilePath}' file.", ex );
                        if( SingleWorld != null ) return false;
                    }
                }
            }
            if( _stackRepos.Count == 0 )
            {
                using( monitor.OpenInfo( "Since there is no Stack defined, we initialize CK and CK-Build mapped to '/Dev/' by default." ) )
                {
                    monitor.Info( $"Use 'run World/{nameof( SetWorldMapping )}' command to change this default mapping if you want." );
                    _stackRepos.Add( new StackRepo( this, new Uri( "https://github.com/signature-opensource/CK-Stack" ), true ) );
                    _stackRepos.Add( new StackRepo( this, new Uri( "https://github.com/CK-Build/CK-Build-Stack" ), true ) );
                    if( WorldLocalMapping.CanSetMapping )
                    {
                        WorldLocalMapping.SetMap( monitor, "CK-Build", "/Dev/CK-Build" );
                        WorldLocalMapping.SetMap( monitor, "CK", "/Dev/CK" );
                    }
                    WriteStacksToLocalStacksFilePath( monitor );
                }
            }
            return true;
        }

        /// <summary>
        /// Updates the "Stacks.xml" file.
        /// Called on each change of the <see cref="StackRepositories"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True on success, false on error.</returns>
        bool WriteStacksToLocalStacksFilePath( IActivityMonitor m )
        {
            Debug.Assert( SingleWorld == null );
            using( m.OpenDebug( $"Saving {StacksFilePath}." ) )
            {
                try
                {
                    new XDocument( new XElement( "Stacks", _stackRepos.Select( r => r.ToXml() ) ) ).Save( StacksFilePath );
                    return true;
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the commands related to RepositoryStacks or world management
        /// must be disabled.
        /// </summary>
        public bool DisableRepositoryAndStacksCommands { get; set; }

        /// <summary>
        /// Whether this is a multiple worlds store and <see cref="DisableRepositoryAndStacksCommands"/> is false.
        /// </summary>
        public bool CanEnsureStackRepository => SingleWorld == null && !DisableRepositoryAndStacksCommands;

        /// <summary>
        /// Registers a new <see cref="StackRepo"/> or updates its <see cref="GitRepositoryKey.IsPublic"/> configuration.
        /// This is exposed as a command by <see cref="MultipleWorldHome.EnsureStackRepository"/> in order for Stack repository manipulations
        /// (that are NOT world: it's meta) to appear in "Home/" command namespace instead of "World/".
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="url">The repository URL. Must not be null or empty.</param>
        /// <param name="isPublic">Whether this repository contains public (Open Source) worlds.</param>
        public void EnsureStackRepository( IActivityMonitor m, string url, bool isPublic )
        {
            if( !CanEnsureStackRepository ) throw new InvalidOperationException( nameof( CanEnsureStackRepository ) );
            if( String.IsNullOrWhiteSpace( url ) || !Uri.TryCreate( url, UriKind.Absolute, out var uri ) ) throw new ArgumentException( $"Must be a valid, absolute, URL: {url}", nameof( url ) );
            uri = ProtoGitFolder.CheckAndNormalizeRepositoryUrl( uri );
            int idx = _stackRepos.IndexOf( r => r.OriginUrl.Equals( uri ) );
            if( idx < 0 )
            {
                var r = new StackRepo( this, uri, isPublic );
                if( r.RefreshMultiple( m, true ).LoadSuccess && r.Worlds.Count > 0 )
                {
                    idx = _stackRepos.IndexOf( r => r.OriginUrl.Equals( uri ) );
                    if( idx < 0 )
                    {
                        _stackRepos.Add( r );
                        WriteStacksToLocalStacksFilePath( m );
                    }
                    else
                    {
                        m.Warn( $"Stack repository '{url}' has already been defined by someone else." );
                    }
                }
                else
                {
                    m.Warn( $"EnsureStackRepository( '{url}', {isPublic} ): no stack added." );
                }
            }
            if( idx >= 0 )
            {
                var r = _stackRepos[idx];
                if( r.IsPublic != isPublic )
                {
                    r.IsPublic = isPublic;
                    m.Info( $"Changing configuration for '{r.OriginUrl}' from {(isPublic ? "Private to Public" : "Public to Private")}." );
                    WriteStacksToLocalStacksFilePath( m );
                }
            }
        }

        /// <summary>
        /// Whether this is a multiple worlds store and <see cref="DisableRepositoryAndStacksCommands"/> is false.
        /// </summary>
        public bool CanDeleteStackRepository => SingleWorld == null && !DisableRepositoryAndStacksCommands;

        /// <summary>
        /// Removes a stack repository.
        /// A warning is emitted if the repository is not registered.
        /// This is exposed as a command by <see cref="MultipleWorldHome.EnsureStackRepository"/> in order for Stack repository manipulations
        /// (that are NOT world: it's meta) to appear in "Home/" command namespace instead of "World/".
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="url">The URL of the repository to remove.</param>
        public void DeleteStackRepository( IActivityMonitor m, string url )
        {
            if( !CanDeleteStackRepository ) throw new InvalidOperationException( nameof( CanDeleteStackRepository ) );
            if( String.IsNullOrWhiteSpace( url ) || !Uri.TryCreate( url, UriKind.Absolute, out var uri ) ) throw new ArgumentException( "Must be a valid URL.", nameof( url ) );
            int idx = _stackRepos.IndexOf( r => r.OriginUrl.AbsoluteUri.Equals( url, StringComparison.OrdinalIgnoreCase ) );
            if( idx < 0 ) m.Warn( $"Stack repository '{url}' not found." );
            else
            {
                m.Info( $"Removing: '{_stackRepos[idx]}' with {_stackRepos[idx].Worlds.Count} world(s) in it." );
                _stackRepos.RemoveAt( idx );
                WriteStacksToLocalStacksFilePath( m );
            }
        }
        
        /// <summary>
        /// Whether this is a multiple worlds store and <see cref="DisableRepositoryAndStacksCommands"/> is false.
        /// </summary>
        public bool CanDestroyWorld => SingleWorld == null && !DisableRepositoryAndStacksCommands;

        /// <summary>
        /// Deletes a stack or a world.
        /// A warning is emitted if the world cannot be found.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="worldFullName">The world full name to remove.</param>
        [CommandMethod]
        public void DestroyWorld( IActivityMonitor m, string worldFullName )
        {
            if( !CanDestroyWorld ) throw new InvalidOperationException( nameof( CanDestroyWorld ) );
            if( !WorldName.TryParse( worldFullName, out var name ) )
            {
                m.Error( $"Invalid '{worldFullName}' world name." );
                return;
            }
            List<WorldInfo>? toRemove = null;
            if( name.ParallelName == null )
            {
                toRemove = _stackRepos.SelectMany( r => r.Worlds ).Where( w => w.WorldName.Name == name.Name ).ToList();
            }
            else
            {
                toRemove = _stackRepos.SelectMany( r => r.Worlds ).Where( w => w.WorldName.FullName == name.FullName ).ToList();
            }
            if( toRemove.Count == 0 )
            {
                m.Warn( $"Unable to find '{worldFullName}' world." );
                return;
            }
            if( toRemove.Count > 1 )
            {
                m.Info( $"Removing '{worldFullName}' Stack: removing '{toRemove.Select( w => w.WorldName.FullName ).Concatenate( "', '" )}' worlds." );
            }
            else
            {
                m.Info( $"Removing '{worldFullName}' world." );
            }
            foreach( var w in toRemove )
            {
                if( !w.Destroy( m ) )
                {
                    m.Error( $"Unable to destroy '{w.WorldName}'. Manual check required in folders '{RootPath}' and '{w.WorldName.Root}'." );
                    break;
                }
            }
            WriteStacksToLocalStacksFilePath( m );
        }

        /// <summary>
        /// Only if this is a multiple worlds store can a World mapping be set.
        /// </summary>
        public bool CanSetWorldMapping => WorldLocalMapping.CanSetMapping;

        /// <summary>
        /// Sets a mapping for a world full name.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="worldFullName">The world full name that must exist.</param>
        /// <param name="mappedPath">The target mapped path.</param>
        /// <returns>True on success, false on error.</returns>
        [CommandMethod]
        public bool SetWorldMapping( IActivityMonitor m, string worldFullName, NormalizedPath mappedPath )
        {
            if( !CanSetWorldMapping ) throw new InvalidOperationException( nameof( CanSetWorldMapping ) );
            var worlds = ReadWorlds( m );
            if( worlds == null ) return false;
            var w = worlds.FirstOrDefault( x => x.FullName.Equals( worldFullName, StringComparison.OrdinalIgnoreCase ) );
            if( w == null )
            {
                m.Error( $"World '{worldFullName}' not found." );
                return false;
            }
            if( w.Root == mappedPath )
            {
                m.Trace( $"World '{w.FullName}' is already mapped to '{w.Root}'." );
                return true;
            }
            if( !mappedPath.IsRooted )
            {
                m.Error( $"Invalid '{mappedPath}'. It must be rooted." );
                return false;
            }
            return WorldLocalMapping.SetMap( m, w.FullName, mappedPath );
        }

        /// <summary>
        /// Gets the list of <see cref="IRootedWorldName"/>, optionally pulling the repositories.
        /// In Single world mode, this method doesn't resynchronize anything and returns the single world.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="withPull">
        /// True to pull the repositories, false to only refresh from the local
        /// working folders of the stores. Ignored in single world mode.
        /// </param>
        public IReadOnlyList<IRootedWorldName> ReadWorlds( IActivityMonitor m, bool withPull )
        {
            if( SingleWorld != null )
            {
                return new[] { _stackRepos[0].Worlds[0].WorldName };
            }
            bool changes = false;
            foreach( var r in _stackRepos )
            {
                var (sucess, hasChanged) = r.RefreshMultiple( m, withPull );
                if( !sucess )
                {
                    m.Warn( $"Unable to open repository '{r}'." );
                }
                else
                {
                    m.Trace( $"Repository '{r}' opened with {r.Worlds.Count} worlds." );
                    changes |= hasChanged;
                }
            }
            if( changes )
            {
                WriteStacksToLocalStacksFilePath( m );
            }
            var list = _stackRepos.SelectMany( r => r.Worlds.Select( w => w.WorldName ) ).ToList();
            Debug.Assert( '[' > 'A', "Unfortunately..." );
            list.Sort( ( w1, w2 ) =>
            {
                int cmp = w1.Name.CompareTo( w2.Name );
                return cmp != 0 ? cmp : string.Compare( w1.ParallelName, w2.ParallelName );
            } );
            return list;
        }

        LocalWorldName ToLocal( IWorldName w )
        {
            if( w is LocalWorldName loc ) return loc;
            throw new ArgumentException( "Must be a valid LocalWorldName.", nameof( w ) );
        }

        public override IReadOnlyList<IRootedWorldName> ReadWorlds( IActivityMonitor m ) => ReadWorlds( m, false );

        public override XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w )
        {
            return XDocument.Load( ToLocal( w ).XmlDescriptionFilePath, LoadOptions.SetLineInfo );
        }

        public override bool WriteWorldDescription( IActivityMonitor m, IWorldName w, XDocument content )
        {
            if( content == null ) throw new ArgumentNullException( nameof( content ) );
            var local = ToLocal( w );
            content.Save( local.XmlDescriptionFilePath );
            OnWorkingFolderChanged( m, local );
            return true;
        }

        protected override IRootedWorldName? DoCreateNewParallel( IActivityMonitor m, IRootedWorldName source, string parallelName, XDocument content )
        {
            Debug.Assert( source != null );
            Debug.Assert( content != null );
            Debug.Assert( !String.IsNullOrWhiteSpace( parallelName ) );

            StackRepo? repo = null;
            if( source is not LocalWorldName src
                || (repo = _stackRepos.FirstOrDefault( r => src.XmlDescriptionFilePath.StartsWith( r.Root ) )) == null )
            {
                m.Error( $"Unable to find source World." );
                return null;
            }
            string wName = source.Name + (parallelName != null ? '[' + parallelName + ']' : String.Empty);
            var world = _stackRepos.SelectMany( r => r.Worlds ).FirstOrDefault( w => w.WorldName.FullName == wName );
            if( world != null )
            {
                m.Error( $"World '{wName}' already exists." );
                return null;
            }

            var path = repo.Root.AppendPart( wName + ".World.xml" );
            if( !File.Exists( path ) )
            {
                var w = new LocalWorldName( path, source.Name, parallelName, WorldLocalMapping );
                if( !WriteWorldDescription( m, w, content ) )
                {
                    m.Error( $"Unable to create {wName} world." );
                    return null;
                }
                return w;
            }
            m.Error( $"World file '{path}' already exists." );
            return null;
        }

        public override SharedWorldState GetOrCreateSharedState( IActivityMonitor m, IWorldName w )
        {
            var p = ToSharedStateFilePath( w ).Item2;
            if( File.Exists( p ) ) return new SharedWorldState( this, w, XDocument.Load( p, LoadOptions.SetLineInfo ) );
            m.Info( $"Creating new shared state for {w.FullName}." );
            return new SharedWorldState( this, w );
        }

        protected override bool SaveSharedState( IActivityMonitor m, IWorldName w, XDocument d )
        {
            var p = ToSharedStateFilePath( w );
            d.Save( p.Item2 );
            OnWorkingFolderChanged( m, p.Item1 );
            return true;
        }

        public override LocalWorldState GetOrCreateLocalState( IActivityMonitor m, IWorldName w )
        {
            var p = ToLocalStateFilePath( w );
            if( File.Exists( p ) ) return new LocalWorldState( this, w, XDocument.Load( p, LoadOptions.SetLineInfo ) );
            m.Info( $"Creating new local state for {w.FullName}." );
            return new LocalWorldState( this, w );
        }

        protected override bool SaveLocalState( IActivityMonitor m, IWorldName w, XDocument d )
        {
            var p = ToLocalStateFilePath( w );
            d.Save( p );
            OnWorkingFolderChanged( m, ToLocal( w ) );
            return true;
        }

        void OnWorkingFolderChanged( IActivityMonitor m, LocalWorldName local )
        {
            var repo = _stackRepos.FirstOrDefault( r => local.XmlDescriptionFilePath.StartsWith( r.Root ) );
            if( repo == null )
            {
                m.Warn( $"Unable to find the local repository for {local.FullName}." );
            }
            else if( !repo.IsOpen )
            {
                m.Warn( $"Local repository {local.FullName} ({repo.Root}) is not opened." );
            }
            else
            {
                repo.PushChanges( m );
            }
        }

        /// <summary>
        /// Computes the path of the shared state file: it is next to the definition file.
        /// </summary>
        /// <param name="w">The world name.</param>
        /// <returns>The LocalWorldName and the path to use to read/write the shared state.</returns>
        (LocalWorldName, NormalizedPath) ToSharedStateFilePath( IWorldName w )
        {
            var local = ToLocal( w );
            var def = local.XmlDescriptionFilePath;
            Debug.Assert( def.EndsWith( ".World.xml" ) );
            return (local, def.RemoveLastPart().AppendPart( def.LastPart.Substring( 0, def.LastPart.Length - 3 ) + "SharedState.xml" ));
        }

        NormalizedPath ToLocalStateFilePath( IWorldName w ) => GetWorkingLocalFolder( w ).AppendPart( "LocalState.xml" );

        /// <summary>
        /// Returns the "$Local" folder in the <see cref="StackRepo"/> repository.
        /// </summary>
        /// <param name="w">The world name. It must exist in this store.</param>
        /// <returns>A local path to an existing and writable directory.</returns>
        public override NormalizedPath GetWorkingLocalFolder( IWorldName w )
        {
            var local = ToLocal( w );
            var def = local.XmlDescriptionFilePath;
            Debug.Assert( def.EndsWith( ".World.xml" ) );
            return def.RemoveLastPart().AppendPart( "$Local" ).AppendPart( w.FullName );
        }

        public void Dispose()
        {
            foreach( var repo in _stackRepos )
            {
                repo.Dispose();
            }
            _stackRepos.Clear();
        }
    }
}
