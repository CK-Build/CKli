using CK.Core;
using CK.Env;
using CK.Env.Analysis;
using CK.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace CKli
{
    class GlobalContext : IDisposable
    {
        readonly IActivityMonitor _monitor;
        readonly XTypedFactory _factory;
        readonly string _rootPath;
        readonly IssueCollector _issues;
        readonly ActionCollector _actions;
        FileSystem _fs;
        XRunnable _root;

        public GlobalContext( IActivityMonitor monitor, XTypedFactory factory, string rootPath )
        {
            _monitor = monitor;
            _factory = factory;
            _rootPath = rootPath;
            _issues = new IssueCollector();
            _actions = new ActionCollector();
        }

        public bool Open()
        {
            Close();
            _fs = new FileSystem( _rootPath );
            var baseProvider = new SimpleServiceContainer();
            baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
            baseProvider.Add( _fs );
            baseProvider.Add( _issues );
            baseProvider.Add( _actions );

            var knownWorldPath = ChooseWorld();
            if( knownWorldPath == null ) return false;
            var original = XDocument.Load( knownWorldPath ).Root;
            var expanded = XTypedFactory.PreProcess( _monitor, original );
            if( expanded.Errors.Count > 0 )
            {
                return false;
            }
            _root = _factory.CreateInstance<XRunnable>( _monitor, expanded.Result, baseProvider );
            return _root != null;
        }

        string ChooseWorld()
        {
            var files = Directory.GetFiles( Path.Combine( _rootPath, "CK-Env" ), "*-World.xml" );
            for(; ;)
            {
                int i = 0;
                foreach( var f in files )
                {
                    Console.WriteLine( $"{++i} - {Path.GetFileName( f )}" );
                }
                Console.WriteLine( "x - Exit" );
                string r = Console.ReadLine();
                if( Int32.TryParse( r, out int result ) && result >= 1 && result <= i ) return files[result - 1];
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
                _fs.Dispose();
                _fs = null;
                _root = null;
                _issues.Clear();
                _actions.Clear();
            }
        }

        public void Dispose() => Close(); 
    }
}
