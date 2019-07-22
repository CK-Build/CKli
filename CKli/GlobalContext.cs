using CK.Core;
using CK.Env;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CKli
{
    public class GlobalContext : IDisposable
    {
        readonly IActivityMonitor _monitor;
        readonly XTypedFactory _factory;
        readonly string _rootPath;
        readonly IWorldStore _worldStore;
        readonly LocalWorldRootPathMapping _localWorldRootPathMapping;
        readonly IBasicApplicationLifetime _appLife;
        FileSystem _fs;
        IWorldName _currentWorld;
        XTypedObject _root;

        class LocalWorldRootPathMapping : ILocalWorldRootPathMapping
        {
            readonly NormalizedPath _rootPath;
            Dictionary<string, NormalizedPath> _map;

            public LocalWorldRootPathMapping( string rootPath )
            {
                _rootPath = rootPath;
                Load();
            }

            public void Load()
            {
                var mapFile = Path.Combine( _rootPath, "CK-Env", "LocalWorldRootPathMapping.txt" );
                _map = File.Exists( mapFile )
                        ? File.ReadAllLines( mapFile )
                        .Select( line => line.Split( '>' ) )
                        .Where( p => p.Length == 2 )
                        .Select( p => (p[0].Trim(), p[1].Trim()) )
                        .Where( p => p.Item1.Length > 0 && p.Item2.Length > 0 )
                        .ToDictionary( p => p.Item1, p => new NormalizedPath( p.Item2 ) )
                        : new Dictionary<string, NormalizedPath>();
                if( !_map.ContainsKey( "CK" ) )
                {
                    if( _rootPath.LastPart != "CK" )
                    {
                        throw new Exception( "When CK is not mapped in LocalWorldRootPathMapping.txt, CK-Env MUST BE ran from a 'CK/CK-Env' folder." );
                    }
                    _map.Add( "CK", _rootPath );
                }
            }

            /// <summary>
            /// Gets the root path for a World.
            /// If the <see cref="IWorldName.FullName"/> is defined, the mapped path is taken as-is.
            /// Otherwise, on a parallel world and if the if the Stack name is mapped (the default world),
            /// we map the parallel world next to the default one.
            /// </summary>
            /// <param name="w">The world name.</param>
            /// <returns>The path to the root directory or null if it is not mapped.</returns>
            public string GetRootPath( IWorldName w )
            {
                NormalizedPath p;
                if( !_map.TryGetValue( w.FullName, out p ) )
                {
                    // If Name is not the same as FullName, we are on a parralel
                    // world that is not mapped: if the Stack name is mapped (the default world),
                    // we map the parrallel world next to the default one.
                    if( _map.TryGetValue( w.Name, out p ) )
                    {
                        p = p.RemoveLastPart().AppendPart( w.FullName );
                    }
                }
                if( !p.IsEmptyPath )
                {
                    Directory.CreateDirectory( p );
                    File.WriteAllText( p.AppendPart( "CKli-World.htm" ), "<html></html>" );
                    return p;
                }
                return null;
            }
        }

        public GlobalContext( IActivityMonitor monitor, XTypedFactory factory, string rootPath, IBasicApplicationLifetime appLife )
        {
            _monitor = monitor;
            _factory = factory;
            _rootPath = rootPath;
            _appLife = appLife;
            CommandRegister = new CommandRegister();
            _localWorldRootPathMapping = new LocalWorldRootPathMapping( rootPath );
            _worldStore = new LocalWorldStore( Path.Combine( _rootPath, "CK-Env" ), _localWorldRootPathMapping );
        }

        public CommandRegister CommandRegister { get; }

        public IWorldName CurrentWorld => _currentWorld;

        public event EventHandler CurrentWorldChanged;

        public bool Open()
        {
            _localWorldRootPathMapping.Load();
            for(; ; )
            {
                Close();
                var (LocalPath, World) = ChooseWorld( _monitor );
                if( LocalPath == null ) return false;

                _currentWorld = null;

                var baseProvider = new SimpleServiceContainer();
                var keyVault = new CKEnvKeyVault( World, LocalPath, CommandRegister );
                _fs = new FileSystem( LocalPath, CommandRegister, keyVault.KeyStore, baseProvider );
                baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
                baseProvider.Add( CommandRegister );
                baseProvider.Add( _fs );
                baseProvider.Add( World );
                baseProvider.Add( _worldStore );
                baseProvider.Add( _appLife );
                baseProvider.Add( keyVault.KeyStore );
                var original = _worldStore.ReadWorldDescription( _monitor, World ).Root;
                var expanded = XTypedFactory.PreProcess( _monitor, original );
                if( expanded.Errors.Count > 0 ) continue;
                _root = _factory.CreateInstance<XTypedObject>( _monitor, expanded.Result, baseProvider );
                if( _root == null ) return false;
                _currentWorld = World;
                var xState = _root.Descendants<XWorldState>().FirstOrDefault();
                if( xState == null )
                {
                    _monitor.Error( "Missing expected WorldState (or XWorldState) element." );
                    continue;
                }
                // Ensures that all required secrets are knwon.
                if( !OpenKeyVault( _monitor, keyVault ) ) continue;
                if( !xState.Initialize( _monitor ) ) continue;
                CurrentWorldChanged?.Invoke( this, EventArgs.Empty );
                return true;
            }
        }

        bool OpenKeyVault( IActivityMonitor m, CKEnvKeyVault vault )
        {
            Debug.Assert( vault.CanOpenKeyVault );
            if( !vault.KeyVaultFileExists )
            {
                m.Warn( $"File '{vault.KeyVaultPath}' is missing. It must be created. " );
            }
            do
            {
                try
                {
                    Console.Write( $"Enter '{vault.KeyVaultKeyName}' pass phrase (empty string to cancel): " );
                    string pass = Console.ReadLine();
                    if( pass.Length == 0 )
                    {
                        m.Warn( "No pass phrase entered." );
                        return false;
                    }
                    if( vault.OpenKeyVault( m, pass ) )
                    {
                        foreach( var vaultKey in vault.KeyStore.Infos )
                        {
                            if( vaultKey.IsSecretAvailable )
                            {
                                m.Info( $"Secret '{vaultKey.Name}' is available. We don't need to ask it." );
                                continue;
                            }
                            if( !vaultKey.FinalSubKey.IsRequired && !vaultKey.IsRequired)
                            {
                                m.Info( $"Secret '{vaultKey.Name}' and its SubKeys are not required. We won't ask it now." );
                                continue;
                            }

                            Console.Write( $"Required secret '{vaultKey}' (empty string to cancel): " );
                            string secret = Console.ReadLine();
                            if( secret.Length == 0 )
                            {
                                m.Warn( "No secret entered. Canceled opening." );
                                return false;
                            }
                            vaultKey.SetSecret( secret );
                        }
                    }
                }
                catch( Exception ex )
                {
                    m.Error( ex );
                }
            }
            while( !vault.IsKeyVaultOpened );
            return true;
        }

        (string LocalPath, IWorldName World) ChooseWorld( IActivityMonitor m )
        {
            var map = new LocalWorldRootPathMapping( _rootPath );
            var worlds = _worldStore.ReadWorlds( m ).Select( ( w, idx ) => (Idx: idx, World: w, LocalPath: map.GetRootPath( w )) ).ToList();
            for(; ; )
            {
                foreach( var g in worlds.GroupBy( f => f.World.Name ) )
                {
                    Console.WriteLine( $"- {g.Key}" );
                    foreach( var (Idx, World, LocalPath) in g )
                    {
                        string key;
                        if( World.ParallelName == null )
                        {
                            key = "<Default>";
                        }
                        else
                        {
                            key = $"[{World.ParallelName}]";
                        }
                        Console.WriteLine( $"   > {Idx + 1} {key} => { LocalPath ?? "(No local mapping)"}" );
                    }
                }
                Console.WriteLine( "-------------" );
                Console.WriteLine( "   > x - Exit" );
                string r = Console.ReadLine();
                if( Int32.TryParse( r, out int result )
                    && result >= 1 && result <= worlds.Count )
                {
                    var (Idx, World, LocalPath) = worlds[result - 1];
                    return (LocalPath, World);
                }
                if( r == "x" ) return (null, null);
            }
        }

        public void Close()
        {
            if( _fs != null )
            {
                if( _root != null )
                {
                    foreach( var e in _root.Descendants<IDisposable>().Reverse() )
                    {
                        e.Dispose();
                    }
                    _root = null;
                }
                CommandRegister.UnregisterAll();
                _fs.Dispose();
                _fs = null;
            }
        }

        public void Dispose() => Close();
    }
}
