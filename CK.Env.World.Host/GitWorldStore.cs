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
    public sealed class GitWorldStore : WorldStore, ICommandMethodsProvider
    {
        readonly NormalizedPath _rootPath;
        readonly List<StackDef> _stacks;
        readonly List<StackRepo> _repos;

        IReadOnlyList<LocalWorldName> _worldNames;
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
            commandRegister.Register( this );
        }

        NormalizedPath ICommandMethodsProvider.CommandProviderName => UserHost.HomeCommandPath;

        new SimpleWorldLocalMapping WorldLocalMapping => (SimpleWorldLocalMapping)base.WorldLocalMapping;

        SecretKeyStore SecretKeyStore { get; }

        NormalizedPath StacksFilePath { get; }

        #region Stack Definition

        /// <summary>
        /// Exposes a stack definition: its name and repository (since this
        /// specializes <see cref="GitRepositoryKey"/>).
        /// </summary>
        public class StackDef : GitRepositoryKey
        {
            internal StackDef( SecretKeyStore secretKeyStore, string stackName, Uri url, bool isPublic, string branchName = "master" )
                : base( secretKeyStore, url, isPublic )
            {
                StackName = stackName;
                BranchName = branchName;
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
        /// Reads the Stacks.txt file, opens the repositories
        /// and reads the list of all the worlds.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        public void Initialize( IActivityMonitor m )
        {
            if( !File.Exists( StacksFilePath ) )
            {
                m.Warn( $"File '{StacksFilePath}' not found." );
                return;
            }
            using( m.OpenInfo( $"Reading '{StacksFilePath}'." ) )
            {
                foreach( var line in File.ReadAllLines( StacksFilePath ) )
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
                                    EnsureStackDefinition( m, name, url, isPublic, branchName );
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
            OnStacksChanged( m, true );
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
        /// <param name="mappedPath">
        /// The mapped path used if no exisitng mapping already exists.
        /// If no mapping already exists, it must be a local rooted path (that may not exist).
        /// </param>
        /// <param name="url">The remote repository url.</param>
        /// <param name="isPublic">Whether this stack is public (Open Source).</param>
        /// <param name="branchName">Optional branch name. Should always be "master".</param>
        [CommandMethod]
        public void EnsureStackDefinition(
            IActivityMonitor m,
            string stackName,
            NormalizedPath mappedPath,
            string url,
            bool isPublic,
            string branchName = "master" )
        {
            if( String.IsNullOrWhiteSpace( stackName ) ) throw new ArgumentException( "Must not be empty.", nameof(stackName) );
            if( !WorldLocalMapping.IsMapped( stackName ) )
            {
                if( !mappedPath.IsRooted ) throw new ArgumentException( "Path must be rooted.", nameof( mappedPath ) );
                WorldLocalMapping.SetMap( stackName, mappedPath );
            }
            EnsureStackDefinition( m, stackName, url, isPublic, branchName );
            OnStacksChanged( m, true );
        }

        void EnsureStackDefinition(
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
                if( _stacks[idx].ToString() == def.ToString() ) return;
                m.Trace( $"Replacing existing: '{_stacks[idx]}'." );
                _stacks[idx] = def;
            }
            else _stacks.Add( def );
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
                OnStacksChanged( m, false );
            }          
        }

        void OnStacksChanged( IActivityMonitor m, bool openRepositories )
        {
            File.WriteAllText( StacksFilePath, _stacks.Select( s => s.ToString() ).Concatenate( Environment.NewLine ) );
            UpdateReposFromDefinitions( m, openRepositories );
        }

        #endregion

        #region Repsitory
        class StackRepo
        {
            public readonly Uri OriginUrl;
            readonly GitWorldStore _store;
            readonly NormalizedPath _root;
            int _syncCount;
            StackDef[] _expectedStacks;

            Repository _git;

            public StackRepo( GitWorldStore store, Uri uri )
            {
                _store = store;
                OriginUrl = uri;
                var cleanPath = uri.AbsolutePath.Replace( "_git", "" ).Replace( "_", "" ).Replace( '/', '_' ).Replace( "__", "_" ).ToLowerInvariant();
                _root = store._rootPath.AppendPart( cleanPath );
            }

            bool IsOpen => _git != null;

            internal void Synchronize(
                IActivityMonitor m,
                IEnumerable<StackDef> expectedStacks,
                bool openRepositories,
                Action<LocalWorldName> addWorld )
            {
                _syncCount = _store._syncCount;
                if( !IsOpen && openRepositories )
                {

                }
                if( IsOpen ) ReadWorlds( m, expectedStacks, addWorld );
            }

            internal bool PostSynchronize( IActivityMonitor m )
            {
                if( _syncCount == _store._syncCount ) return true;
                using( m.OpenInfo( $"Removing stack repository '{_root.LastPart}' => '{OriginUrl}'." ) )
                {
                    try
                    {
                        if( IsOpen )
                        {
                            _git.Dispose();
                            _git = null;
                        }
                        FileHelper.RawDeleteLocalDirectory( m, _root );
                    }
                    catch( Exception ex )
                    {
                        m.Error( ex );
                    }
                }
                return false;
            }

            void ReadWorlds( IActivityMonitor m, IEnumerable<StackDef> expectedStacks, Action<LocalWorldName> addWorld )
            {
                Debug.Assert( IsOpen );
                var worldNames = Directory.GetFiles( _root, "*.World.xml" )
                                    .Select( p => LocalWorldName.TryParse( m, p, _store.WorldLocalMapping ) )
                                    .Where( w => w != null )
                                    .ToList();
                var missing = expectedStacks
                                .Where( s => !worldNames.Any( w => w.FullName.Equals( s.StackName, StringComparison.OrdinalIgnoreCase ) ) );
                foreach( var s in missing )
                {
                    m.Warn( $"Unable to find xml file definition for '{s.StackName}'." );
                }
                for( int i = 0; i < worldNames.Count; ++i )
                {
                    var w = worldNames[i];
                    if( w.ParallelName == null
                        && !expectedStacks.Any( s => s.StackName.Equals( w.FullName, StringComparison.OrdinalIgnoreCase ) ) )
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
        }

        void UpdateReposFromDefinitions( IActivityMonitor m, bool openRepositories )
        {
            var newWorlds = new List<LocalWorldName>();
            ++_syncCount;
            foreach( var gD in _stacks.GroupBy( d => d.OriginUrl ) )
            {
                EnsureRepo( gD.Key ).Synchronize( m, gD, openRepositories, newWorlds.Add );
            }
            for( int i = 0; i < _repos.Count; ++i )
            {
                if( !_repos[i].PostSynchronize( m ) )
                {
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
            WorldLocalMapping.SetMap( w.FullName, mappedPath );
            m.Info( $"World '{w.FullName}' is now mapped to '{mappedPath}'." );
            return Refresh( m, false );
        }

        /// <summary>
        /// Refreshes the list of <see cref="ReadWorlds"/>, optionally pulling the repositories.
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <param name="withPull">
        /// True to pull the repositories, false to only refresh from the local
        /// working folder.
        /// </param>
        /// <returns>True on success, falseon error.</returns>
        public bool Refresh( IActivityMonitor m, bool withPull )
        {

        }
 
        void SetWorlds( IActivityMonitor m, List<LocalWorldName> newWorlds )
        {
            newWorlds.Sort( ( w1, w2 ) => w1.FullName.CompareTo( w2.FullName ) );
            _worldNames = newWorlds;
        }

        public override IReadOnlyList<IRootedWorldName> ReadWorlds( IActivityMonitor m )
        {
            throw new NotImplementedException();
        }

        public override SharedWorldState GetOrCreateSharedState( IActivityMonitor m, IWorldName w )
        {
            throw new NotImplementedException();
        }

        public override XDocument ReadWorldDescription( IActivityMonitor m, IWorldName w )
        {
            throw new NotImplementedException();
        }

        public override bool WriteWorldDescription( IActivityMonitor m, IWorldName w, XDocument content )
        {
            throw new NotImplementedException();
        }

        protected override LocalWorldName DoCreateNew( IActivityMonitor m, string name, string parallelName, XDocument content )
        {
            throw new NotImplementedException();
        }

        protected override bool SaveSharedState( IActivityMonitor m, IWorldName w, XDocument d )
        {
            throw new NotImplementedException();
        }
    }
}
