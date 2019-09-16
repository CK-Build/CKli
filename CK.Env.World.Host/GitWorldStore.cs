using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CK.Core;
using CK.Text;
using LibGit2Sharp;

namespace CK.Env
{
    public sealed class GitWorldStore : WorldStore, ICommandMethodsProvider, IDisposable
    {
        readonly NormalizedPath _rootPath;
        readonly List<StackDef> _stacks;
        readonly List<StackRepo> _repos;

        int _syncCount;

        public GitWorldStore(
            NormalizedPath userHostPath,
            SimpleWorldLocalMapping mapping,
            SecretKeyStore keyStore,
            CommandRegister commandRegister )
            : base( mapping )
        {
            _rootPath = userHostPath;
            SecretKeyStore = keyStore;
            StacksFilePath = userHostPath.AppendPart( "Stacks.txt" );
            _stacks = new List<StackDef>();
            Stacks = _stacks;
            _repos = new List<StackRepo>();
            commandRegister.Register( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => UserHost.HomeCommandPath;

        new SimpleWorldLocalMapping WorldLocalMapping => (SimpleWorldLocalMapping)base.WorldLocalMapping;

        SecretKeyStore SecretKeyStore { get; }

        NormalizedPath StacksFilePath { get; }

        /// <summary>
        /// Reads the Stacks.txt file and instanciates the <see cref="StackDef"/> objects:
        /// this registers the required secrets in the key store.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void ReadStacksFromFile( IActivityMonitor m )
        {
            if( !File.Exists( StacksFilePath ) )
            {
                m.Warn( $"File '{StacksFilePath}' not found." );
            }
            else using( m.OpenInfo( $"Reading '{StacksFilePath}'." ) )
            {
                foreach( var line in File.ReadAllLines( StacksFilePath ).Where( line => !String.IsNullOrWhiteSpace( line ) ) )
                {
                    Exception severe = null;
                    try
                    {
                        var l = line.Split( '>' );
                        if( l.Length >= 3 )
                        {
                            var name = l[0].Trim();
                            if( name.Length > 0 )
                            {
                                var url = l[1].Trim();
                                if( Uri.TryCreate( url, UriKind.Absolute, out var uri ) )
                                {
                                    var pubTxt = l[2].Trim();
                                    bool isPublic = pubTxt.Equals( "Public", StringComparison.OrdinalIgnoreCase );
                                    string branchName = l.Length >= 4 ? l[3].Trim() : null;
                                    FindOrCreateStackDef( m, name, url, isPublic, branchName );
                                    continue;
                                }
                                else m.Error( $"Invalid repository url '{url}'." );
                            }
                            else m.Error( $"Missing stack name." );
                        }
                        else m.Error( $"Invalid line format." );
                    }
                    catch( Exception ex )
                    {
                        severe = ex;
                    }
                    m.Error( $"Unable to read line '{line}'.", severe );
                }
            }
            if( _stacks.Count == 0 )
            {
                using( m.OpenInfo( "Since there is no Stack defined, we initialize CK and CK-Build mapped to '/Dev/CK' by default." ) )
                {
                    m.Info( $"Use Home/{nameof( SetWorldMapping )} command to update the mapping." );
                    EnsureStackDefinition( m, "CK-Build", "https://github.com/signature-opensource/CK-Stack.git", true, "/Dev/CK" );
                    EnsureStackDefinition( m, "CK", "https://github.com/signature-opensource/CK-Stack.git", true, "/Dev/CK" );
                }
            }
            else UpdateReposFromDefinitions( m, StackInitializeOption.None );
        }

        /// <summary>
        /// Must be called after <see cref="ReadStacksFromFile(IActivityMonitor)"/>
        /// and after beeing sure that all required secrets are available.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void Initialize( IActivityMonitor m )
        {
            UpdateReposFromDefinitions( m, StackInitializeOption.OpenAndPullRepository );
        }

        void WriteStacksToFile( IActivityMonitor m )
        {
            File.WriteAllLines( StacksFilePath, _stacks.Select( s => s.ToString() ) );
        }

        #region Stack Definition

        /// <summary>
        /// Exposes a stack definition: its name and repository (since this
        /// specializes <see cref="GitRepositoryKey"/>).
        /// </summary>
        public class StackDef : GitRepositoryKey
        {
            internal StackDef( SecretKeyStore secretKeyStore, string stackName, Uri url, bool isPublic, string branchName = null )
                : base( secretKeyStore, url, isPublic )
            {
                StackName = stackName;
                BranchName = branchName ?? "master";
            }

            /// <summary>
            /// Gets the stack name.
            /// </summary>
            public string StackName { get; }

            /// <summary>
            /// Gets the branch name: Should always be "master" but this may be changed.
            /// </summary>
            public string BranchName { get; }

            /// <summary>
            /// Overridden to return the one line format saved in tthe text file.
            /// </summary>
            /// <returns>This defintion on one line.</returns>
            public override string ToString()
            {
                var m = $"{StackName} > {OriginUrl} > ";
                m += IsPublic ? "Public" : "Private";
                if( BranchName != "master" ) m += " > " + BranchName;
                return m;
            }
        }

        /// <summary>
        /// Gets the stacks definition.
        /// </summary>
        public IReadOnlyCollection<StackDef> Stacks { get; }

        /// <summary>
        /// Registers a new stack in <see cref="Stacks"/> or updates it.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="stackName">The name of the stack. This is the key.</param>
        /// <param name="url">The remote repository url.</param>
        /// <param name="isPublic">Whether this stack is public (Open Source). This will set whether the git repository hosting the stack is public or not</param>
        /// <param name="mappedPath">
        /// The mapped path used if no existing mapping already exists.
        /// This is required if no mapping currently already exists, it must be a local rooted path.
        /// </param>
        /// <param name="branchName">Optional branch name. Should always be "master".</param>
        [CommandMethod]
        public void EnsureStackDefinition(
            IActivityMonitor m,
            string stackName,
            string url,
            bool isPublic,
            NormalizedPath mappedPath = default,
            string branchName = "master")
        {
            if( String.IsNullOrWhiteSpace( stackName ) ) throw new ArgumentException( "Must not be empty.", nameof(stackName) );

            bool change = WorldLocalMapping.SetMap( m, stackName, mappedPath );
            if( FindOrCreateStackDef( m, stackName, url, isPublic, branchName ) ) change = true;

            if( change )
            {
                UpdateReposFromDefinitions( m, StackInitializeOption.None );
                WriteStacksToFile( m );
            }
        }

        /// <summary>
        /// Returns true it the definition has been added or updated.
        /// False if nothing changed.
        /// </summary>
        bool FindOrCreateStackDef(
                        IActivityMonitor m,
                        string stackName,
                        string url,
                        bool isPublic,
                        string branchName )
        {
            var def = new StackDef( SecretKeyStore, stackName, new Uri( url ), isPublic, branchName );
            int idx = _stacks.IndexOf( d => d.StackName.Equals( stackName, StringComparison.OrdinalIgnoreCase ) );
            if( idx >= 0 )
            {
                if( _stacks[idx].ToString() == def.ToString() ) return false;
                m.Info( $"Replaced existing Stack: '{_stacks[idx]}' with '{def}'." );
                _stacks[idx] = def;
            }
            else
            {
                m.Info( $"Added Stack: '{def}'." );
                _stacks.Add( def );
            }
            return true;
        }

        /// <summary>
        /// Removes a stack.
        /// A warning is emitted if the stack is not found in the registered <see cref="Stacks"/>.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="stackName">The name of the stack to remove.</param>
        [CommandMethod]
        public void DeleteStackDefinition( IActivityMonitor m, string stackName )
        {
            int idx = _stacks.IndexOf( d => d.StackName.Equals( stackName, StringComparison.OrdinalIgnoreCase ) );
            if( idx < 0 ) m.Warn( $"Stack named '{stackName}' not found." );
            else
            {
                m.Info( $"Removing: '{_stacks[idx]}'." );
                _stacks.RemoveAt( idx );
                UpdateReposFromDefinitions( m, StackInitializeOption.None );
            }
        }

        #endregion

        #region Repository
        class StackRepo : IDisposable
        {
            public readonly Uri OriginUrl;
            readonly GitWorldStore _store;
            int _syncCount;

            StackDef[] _stacks;
            GitRepository _git;
            
            public StackRepo( GitWorldStore store, Uri uri )
            {
                _store = store;
                OriginUrl = uri;
                var cleanPath = uri.AbsolutePath
                                   .Replace( ".git", "" )
                                   .Replace( "_git", "" )
                                   .Replace( '/', '_' )
                                   .Replace( ':', '_')
                                   .Replace( "__", "_" )
                                   .Trim( '_' )
                                   .ToLowerInvariant();
                Root = store._rootPath.AppendPart( cleanPath );
            }

            /// <summary>
            /// Pulls the remote and returns true if the working folder has been updated.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <returns>True if the working folder has been updated.</returns>
            internal bool Pull( IActivityMonitor m )
            {
                return EnsureOpen( m ) && _git.Pull( m, MergeFileFavor.Theirs ).ReloadNeeded;
            }

            /// <summary>
            /// Commits and push changes to the remote.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <returns>True on success, false on error.</returns>
            internal bool PushChanges( IActivityMonitor m )
            {
                Debug.Assert( IsOpen );
                return _git.Commit( m, "Automatic synchronization commit." )
                       && _git.Push( m );
            }

            /// <summary>
            /// Return true if the Git Repository is open.
            /// </summary>
            public bool IsOpen => _git != null;

            public NormalizedPath Root { get; }

            /// <summary>
            /// Ensure that the the Git Repository is Opened.
            /// Return <see cref="IsOpen"/>.
            /// </summary>
            /// <param name="m">The monitor to use.</param>
            /// <returns><see cref="IsOpen"/></returns>
            internal bool EnsureOpen( IActivityMonitor m )
            {
                if( !IsOpen )
                {
                    var gitKey = _stacks[0];
                    Debug.Assert( _stacks.All( d => d.OriginUrl == gitKey.OriginUrl && d.IsPublic == gitKey.IsPublic ) );
                    Debug.Assert( _stacks.All( d => d.BranchName == gitKey.BranchName ) );
                    _git = GitRepository.Create( m, gitKey, Root, Root.LastPart, false, gitKey.BranchName, true );
                }
                return IsOpen;
            }

            internal void Synchronize(
                IActivityMonitor m,
                IEnumerable<StackDef> expectedStacks,
                StackInitializeOption option,
                Action<LocalWorldName> addWorld )
            {
                Debug.Assert( expectedStacks.All( d => d.OriginUrl == OriginUrl ) );
                _syncCount = _store._syncCount;
                _stacks = expectedStacks.ToArray();
                ReadWorlds( m, option, addWorld );
            }

            internal bool PostSynchronize( IActivityMonitor m )
            {
                if( _syncCount == _store._syncCount ) return true;
                using( m.OpenInfo( $"Removing stack repository '{Root.LastPart}' => '{OriginUrl}'." ) )
                {
                    try
                    {
                        if( IsOpen )
                        {
                            _git.Dispose();
                            _git = null;
                        }
                        FileHelper.RawDeleteLocalDirectory( m, Root );
                    }
                    catch( Exception ex )
                    {
                        m.Error( ex );
                    }
                }
                return false;
            }

            internal void ReadWorlds( IActivityMonitor m, StackInitializeOption option, Action<LocalWorldName> addWorld )
            {
                if( option == StackInitializeOption.OpenRepository ) EnsureOpen( m );
                else if( option == StackInitializeOption.OpenAndPullRepository ) Pull( m );
                if( !IsOpen ) return;

                var worldNames = Directory.GetFiles( Root, "*.World.xml" )
                                    .Select( p => LocalWorldName.TryParse( m, p, _store.WorldLocalMapping ) )
                                    .Where( w => w != null )
                                    .ToList();
                var missing = _stacks
                                .Where( s => !worldNames.Any( w => w.FullName.Equals( s.StackName, StringComparison.OrdinalIgnoreCase ) ) );
                foreach( var s in missing )
                {
                    m.Warn( $"Unable to find xml file definition for '{s.StackName}'." );
                }
                for( int i = 0; i < worldNames.Count; ++i )
                {
                    var w = worldNames[i];
                    if( w.ParallelName == null
                        && !_stacks.Any( s => s.StackName.Equals( w.FullName, StringComparison.OrdinalIgnoreCase ) ) )
                    {
                        m.Warn( $"Unexpected '{w.FullName}' stack found. It is ignored." );
                        worldNames.RemoveAt( i-- );
                    }
                    else
                    {
                        addWorld( w );
                    }
                }
            }

            public void Dispose()
            {
                if(IsOpen)
                {
                    _git.Dispose();
                    _git = null;
                }
            }
        }


        void UpdateReposFromDefinitions( IActivityMonitor m, StackInitializeOption option )
        {
            var newWorlds = new List<LocalWorldName>();
            ++_syncCount;
            foreach( var gD in _stacks.GroupBy( d => d.OriginUrl ) )
            {
                if( gD.Any( d => d.IsPublic ) && gD.Any( d => !d.IsPublic ) )
                {
                    m.Error( $"Repository {gD.Key} contains a mix of public and private Stacks. All stacks in this repository are ignored." );
                }
                else if( gD.Select( d => d.BranchName ).Distinct().Count() > 1 )
                {
                    m.Error( $"Repository {gD.Key} contains Stacks bound to different branches: it must be the same branch for all stacks. All stacks in this repository are ignored." );
                }
                else
                {
                    EnsureRepo( gD.Key ).Synchronize( m, gD, option, newWorlds.Add );
                }
            }
            for( int i = 0; i < _repos.Count; ++i )
            {
                if( !_repos[i].PostSynchronize( m ) )
                {
                    _repos[i].Dispose();
                    _repos.RemoveAt( i-- );                  
                }
            }
            SetWorlds( m, newWorlds );

            StackRepo EnsureRepo( Uri u )
            {
                int idx = _repos.IndexOf( r => r.OriginUrl == u );
                if( idx >= 0 ) return _repos[idx];
                var newOne = new StackRepo( this, u );
                _repos.Add( newOne );
                return newOne;
            }
        }

        #endregion

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
            if( !WorldLocalMapping.SetMap( m, w.FullName, mappedPath ) ) return true;
            m.Info( $"World '{w.FullName}' is now mapped to '{mappedPath}'." );
            ReadWorlds( m, false );
            return true;
        }

        StackRepo FindRepo( IActivityMonitor m, string stackName )
        {
            int idx = _stacks.IndexOf( d => d.StackName == stackName );
            if( idx < 0 )
            {
                m.Error( $"Stack named '{stackName}' not found." );
                return null;
            }
            var home = _repos.FirstOrDefault( r => r.OriginUrl == _stacks[idx].OriginUrl );
            if( home == null )
            {
                m.Error( $"Repository initialization error for stack '{_stacks[idx]}'." );
                return null;
            }
            return home;
        }

        /// <summary>
        /// Gets the list of <see cref="ReadWorlds"/>, optionally pulling the repositories.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="withPull">
        /// True to pull the repositories, false to only refresh from the local
        /// working folders.
        /// </param>
        public IReadOnlyList<IRootedWorldName> ReadWorlds( IActivityMonitor m, bool withPull )
        {
            var newWorlds = new List<LocalWorldName>();
            foreach( var r in _repos )
            {
                r.ReadWorlds( m, withPull ? StackInitializeOption.OpenAndPullRepository : StackInitializeOption.OpenRepository, newWorlds.Add );
            }
            return SetWorlds( m, newWorlds );
        }

        IReadOnlyList<IRootedWorldName> SetWorlds( IActivityMonitor m, List<LocalWorldName> newWorlds )
        {
            Debug.Assert( '[' > 'A', "Unfortunately..." );
            newWorlds.Sort( ( w1, w2 ) =>
            {
                int cmp = w1.Name.CompareTo( w2.Name );
                return cmp != 0 ? cmp : w1.ParallelName.CompareTo( w2.ParallelName );
            } );
            return newWorlds;
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

        protected override LocalWorldName DoCreateNew( IActivityMonitor m, string name, string parallelName, XDocument content )
        {
            Debug.Assert( !String.IsNullOrWhiteSpace( name ) );
            Debug.Assert( content != null );
            Debug.Assert( parallelName == null || !String.IsNullOrWhiteSpace( parallelName ) );

            string wName = name + (parallelName != null ? '[' + parallelName + ']' : String.Empty);
            if( ReadWorlds( m ).Any( w => w.FullName == wName ) )
            {
                m.Error( $"World '{wName}' already exists." );
                return null;
            }
            int idx = _stacks.IndexOf( d => d.StackName == name );
            if( idx < 0 )
            {
                m.Error( "A repository must be created first for a new Stack." );
                return null;
            }
            var home = FindRepo( m, name );
            if( home == null ) return null;

            var path = home.Root.AppendPart( wName + ".World.xml" );
            if( !File.Exists( path ) )
            {
                var w = new LocalWorldName( path, name, parallelName, WorldLocalMapping );
                if( !WriteWorldDescription( m, w, content ) )
                {
                    m.Error( $"Unable to create {wName} world." );
                    return null;
                }
                return w;
            }
            m.Error( $"World file {path} already exists." );
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

        void OnWorkingFolderChanged( IActivityMonitor m, LocalWorldName local )
        {
            var repo = _repos.FirstOrDefault( r => local.XmlDescriptionFilePath.StartsWith( r.Root ) );
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

        /// <summary>
        /// Pulls all repositories and returns true if something changed.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>True if something changed.</returns>
        public bool PullAll( IActivityMonitor m )
        {
            bool changed = false;
            foreach( var r in _repos )
            {
                changed |= r.Pull( m );
            }
            return changed;
        }

        public void Dispose()
        {
            foreach( var repo in _repos )
            {
                repo.Dispose();
            }
        }
    }
}
