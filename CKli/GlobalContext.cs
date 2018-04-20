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
        readonly IssueCollector _issues;
        readonly ActionCollector _actions;
        FileSystem _fs;
        XRunnable _root;
        World _currentWorld;

        public class World
        {
            public string FilePath { get; }

            public string WorldName { get; }

            /// <summary>
            /// Gets the LTS key. Normalized to null for current.
            /// </summary>
            public string LTSKey { get; }

            public string DevelopBranchName { get; }

            public string MasterBranchName { get; }

            public string DevelopLocalBranchName { get; }

            public World( string path, string worldName, string ltsKey )
            {
                if( String.IsNullOrWhiteSpace( path ) ) throw new ArgumentNullException( nameof( path ) );
                if( String.IsNullOrWhiteSpace( worldName ) ) throw new ArgumentNullException( nameof( worldName ) );
                FilePath = path;
                WorldName = worldName;
                if( !String.IsNullOrWhiteSpace( ltsKey ) )
                {
                    LTSKey = ltsKey;
                    MasterBranchName = "master-" + ltsKey;
                    DevelopBranchName = "develop-" + ltsKey;
                    DevelopLocalBranchName = $"develop-{ltsKey}-local";
                }
                else
                {
                    MasterBranchName = "master";
                    DevelopBranchName = "develop";
                    DevelopLocalBranchName = $"develop-local";
                }
            }

            public static World Parse( string filePath )
            {
                Debug.Assert( filePath.EndsWith( "-World.xml" ) );
                var fName = Path.GetFileName( filePath );
                fName = fName.Substring( 0, fName.Length - 10 );
                int idx = fName.IndexOf( '-' );
                if( idx < 0 )
                {
                    return new World(filePath, fName, null);
                }
                return new World( filePath, fName.Substring( 0, idx ), fName.Substring( idx + 1 ) );
            }
        }

        public GlobalContext( IActivityMonitor monitor, XTypedFactory factory, string rootPath )
        {
            _monitor = monitor;
            _factory = factory;
            _rootPath = rootPath;
            _issues = new IssueCollector();
            _actions = new ActionCollector();
        }

        public World CurrentWorld => _currentWorld;

        public event EventHandler CurrentWorldChanged;


        public bool Open()
        {
            Close();
            var w = ChooseWorld();
            if( w == null ) return false;

            _currentWorld = null;
            _fs = new FileSystem( _rootPath );
            var baseProvider = new SimpleServiceContainer();
            baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
            baseProvider.Add( _fs );
            baseProvider.Add( w );
            baseProvider.Add( _issues );
            baseProvider.Add( _actions );

            var original = XDocument.Load( w.FilePath ).Root;
            var expanded = XTypedFactory.PreProcess( _monitor, original );
            if( expanded.Errors.Count > 0 )
            {
                return false;
            }
            _root = _factory.CreateInstance<XRunnable>( _monitor, expanded.Result, baseProvider );
            if( _root == null ) return false;
            _currentWorld = w;
            CurrentWorldChanged?.Invoke( this, EventArgs.Empty );
            return true;
        }

        World ChooseWorld()
        {
            var files = Directory.GetFiles( Path.Combine( _rootPath, "CK-Env" ), "*-World.xml" )
                                .Select( ( p, idx ) => (Idx: idx, File: World.Parse( p )) );
            for(; ;)
            {
                int i = 0;
                foreach( var g in files.GroupBy( f => f.File.WorldName ) )
                {
                    Console.WriteLine( $"- {g.Key}" );
                    foreach( var lts in g )
                    {
                        Console.WriteLine( $"- {lts.Idx} - {lts.File.LTSKey ?? "<Current>"}" );
                    }
                }
                Console.WriteLine( "x - Exit" );
                string r = Console.ReadLine();
                if( Int32.TryParse( r, out int result ) && result >= 1 && result <= i )
                {
                    return files.First( f => f.Idx == result ).File;
                }
                if( r == "x" ) return null;
            }
        }

        public bool Run( bool withIssues )
        {
            if( withIssues )
            {
                _issues.Clear();
            }
            _issues.Disabled = !withIssues;
            var runContext = new XRunnable.DefaultContext( _monitor );
            if( !_root.Run( runContext ) )
            {
                _monitor.Error( "Failed Run." );
                return false;
            }
            return true;
        }

        public void DisplayIssues( TextWriter w, bool withDescription ) => _issues.DisplayIssues( w, withDescription );

        public void DisplayActions( TextWriter w )
        {
            w.WriteLine( $"{_actions.Actions.Count} Actions:" );
            foreach( var a in _actions.Actions )
            {
                w.Write( " > " );
                w.WriteLine( a.ToString() );
            }
        }

        public IReadOnlyList<IIssue> Issues => _issues.Issues;

        public IReadOnlyList<XAction> Actions => _actions.Actions;

        public void RunAction( IActivityMonitor monitor, int idxAction )
        {
            var a = _actions.Actions[idxAction];
            using( monitor.OpenInfo( $"Executing action: {a.ToString()}" ) )
            {
                if( a.Parameters.Count > 0 )
                {
                    Console.WriteLine( $"{a.Parameters.Count} parameters required (type '!cancel' to cancel):" );
                    foreach( var p in a.Parameters )
                    {
                        const string prefix = "     ";
                        StringBuilder b = new StringBuilder();
                        if( !String.IsNullOrWhiteSpace( p.Description ) )
                        {
                            b.AppendMultiLine( prefix, p.Description, true, true );
                        }
                        else b.Append( prefix );
                        b.Append( "> " ).Append( p.Name ).Append( " => " );
                        Console.Write( b.ToString() );
                        string s;
                        do
                        {
                            s = Console.ReadLine();
                            if( s == "!cancel" )
                            {
                                monitor.CloseGroup( "Cancelled" );
                                return;
                            }
                        }
                        while( !p.ParseAndSet( monitor, s ) );
                    }
                }
                if( !a.Run( monitor ) )
                {
                    monitor.CloseGroup( "Failed." );
                }
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
                _fs.Dispose();
                _fs = null;
                _issues.Clear();
                _actions.Clear();
            }
        }

        public void Dispose() => Close(); 
    }
}
