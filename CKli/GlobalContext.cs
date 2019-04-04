using CK.Core;
using CK.Env;
using System;
using System.Collections.Generic;
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
            readonly string _rootPath;
            Dictionary<string, string> _map;

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
                        .ToDictionary( p => p.Item1, p => p.Item2 )
                        : new Dictionary<string, string>();
                if( !_map.ContainsKey( "CK" ) )
                {
                    _map.Add( "CK", _rootPath );
                }
            }

            public string GetRootPath( IWorldName w )
            {
                if( _map.TryGetValue( w.FullName, out string p ) )
                {
                    Directory.CreateDirectory( p );
                    File.WriteAllText( Path.Combine( p, "CK-World.htm" ), "<html></html>" );
                }
                return p;
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
            Close();
            _localWorldRootPathMapping.Load();
            var (LocalPath, World) = ChooseWorld( _monitor );
            if( LocalPath == null ) return false;

            _currentWorld = null;

            var baseProvider = new SimpleServiceContainer();
            _fs = new FileSystem( LocalPath, CommandRegister, baseProvider );
            var keyStore = new CKEnvKeyVault( World, _fs, CommandRegister );
            baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
            baseProvider.Add( CommandRegister );
            baseProvider.Add( _fs );
            baseProvider.Add( World );
            baseProvider.Add( _worldStore );
            baseProvider.Add( _appLife );
            baseProvider.Add<ISecretKeyStore>( keyStore );
            var original = _worldStore.ReadWorldDescription( _monitor, World ).Root;
            var expanded = XTypedFactory.PreProcess( _monitor, original );
            if( expanded.Errors.Count > 0 )
            {
                return false;
            }
            _root = _factory.CreateInstance<XTypedObject>( _monitor, expanded.Result, baseProvider );
            if( _root == null ) return false;
            _currentWorld = World;
            CommandRegister["World/Initialize"].Execute( _monitor, null );
            CurrentWorldChanged?.Invoke( this, EventArgs.Empty );
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
                        Console.WriteLine( $"   > {Idx + 1} - {World.LTSKey ?? "<Current>"} => { LocalPath ?? "(No local mapping)"}" );
                    }
                }
                Console.WriteLine( "   > x - Exit" );
                string r = Console.ReadLine();
                if( Int32.TryParse( r, out int result )
                    && result >= 1 && result <= worlds.Count )
                {
                    var (Idx, World, LocalPath) = worlds[result - 1];
                    return (LocalPath, World);
                }
                if( r == "x" ) return (null,null);
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
