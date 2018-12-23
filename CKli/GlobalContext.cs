using CK.Core;
using CK.Env;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli
{
    public class GlobalContext : IDisposable
    {
        readonly IActivityMonitor _monitor;
        readonly XTypedFactory _factory;
        readonly string _rootPath;
        readonly IWorldStore _worldStore;
        readonly LocalWorldRootPathMapping _localWorldRootPathMapping;
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

        public GlobalContext( IActivityMonitor monitor, XTypedFactory factory, string rootPath )
        {
            _monitor = monitor;
            _factory = factory;
            _rootPath = rootPath;
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
            var w = ChooseWorld( _monitor );
            if( w.LocalPath == null ) return false;

            _currentWorld = null;
            _fs = new FileSystem( w.LocalPath, CommandRegister );
            var baseProvider = new SimpleServiceContainer();
            baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
            baseProvider.Add( CommandRegister );
            baseProvider.Add( _fs );
            baseProvider.Add( w.World );
            baseProvider.Add( _worldStore );

            var original = _worldStore.ReadWorldDescription( _monitor, w.World ).Root;
            var expanded = XTypedFactory.PreProcess( _monitor, original );
            if( expanded.Errors.Count > 0 )
            {
                return false;
            }
            _root = _factory.CreateInstance<XTypedObject>( _monitor, expanded.Result, baseProvider );
            if( _root == null ) return false;
            _currentWorld = w.World;
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
                    foreach( var lts in g )
                    {
                        Console.WriteLine( $"   > {lts.Idx + 1} - {lts.World.LTSKey ?? "<Current>"} => { lts.LocalPath ?? "(No local mapping)"}" );
                    }
                }
                Console.WriteLine( "   > x - Exit" );
                string r = Console.ReadLine();
                if( Int32.TryParse( r, out int result )
                    && result >= 1 && result <= worlds.Count )
                {
                    var c = worlds[result - 1];
                    return (c.LocalPath, c.World);
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
