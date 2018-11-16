using CK.Core;
using CK.Env;
using CK.Env.Analysis;
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
        FileSystem _fs;
        IWorldName _currentWorld;
        XTypedObject _root;

        public GlobalContext( IActivityMonitor monitor, XTypedFactory factory, string rootPath )
        {
            _monitor = monitor;
            _factory = factory;
            _rootPath = rootPath;
            CommandRegister = new CommandRegister();
            _worldStore = new LocalWorldStore( Path.Combine( _rootPath, "CK-Env" ) );
        }

        public CommandRegister CommandRegister { get; }

        public IWorldName CurrentWorld => _currentWorld;

        public event EventHandler CurrentWorldChanged;

        public bool Open()
        {
            Close();
            var w = ChooseWorld( _monitor );
            if( w == null ) return false;

            _currentWorld = null;
            _fs = new FileSystem( _rootPath, CommandRegister );
            var baseProvider = new SimpleServiceContainer();
            baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
            baseProvider.Add( CommandRegister );
            baseProvider.Add( _fs );
            baseProvider.Add( w );
            baseProvider.Add( _worldStore );

            var original = _worldStore.ReadWorldDescription( _monitor, w ).Root;
            var expanded = XTypedFactory.PreProcess( _monitor, original );
            if( expanded.Errors.Count > 0 )
            {
                return false;
            }
            _root = _factory.CreateInstance<XTypedObject>( _monitor, expanded.Result, baseProvider );
            if( _root == null ) return false;
            _currentWorld = w;
            CurrentWorldChanged?.Invoke( this, EventArgs.Empty );
            return true;
        }

        IWorldName ChooseWorld(IActivityMonitor m)
        {
            var worlds = _worldStore.ReadWorlds( m ).Select( (w,idx) => (Idx:idx, World:w) ).ToList();
            for(; ;)
            {
                foreach( var g in worlds.GroupBy( f => f.World.Name ) )
                {
                    Console.WriteLine( $"- {g.Key}" );
                    foreach( var lts in g )
                    {
                        Console.WriteLine( $"   > {lts.Idx+1} - {lts.World.LTSKey ?? "<Current>"}" );
                    }
                }
                Console.WriteLine( "   > x - Exit" );
                string r = Console.ReadLine();
                if( Int32.TryParse( r, out int result ) && result >= 1 && result <= worlds.Count )
                {
                    return worlds[result-1].World;
                }
                if( r == "x" ) return null;
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
