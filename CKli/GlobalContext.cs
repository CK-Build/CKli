using CK.Core;
using CK.Env;
using CK.Env.Analysis;
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
        readonly List<XEnvAction> _actions;
        FileSystem _fs;
        XRunnable _root;

        public GlobalContext( IActivityMonitor monitor, XTypedFactory factory, string rootPath )
        {
            _monitor = monitor;
            _factory = factory;
            _rootPath = rootPath;
            _issues = new IssueCollector();
            _actions = new List<XEnvAction>();
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
            var knownWorldPath = Path.Combine( _rootPath, "CK-Env", "KnownWorld.xml" );
            var original = XDocument.Load( knownWorldPath ).Root;
            var expanded = XTypedFactory.PreProcess( _monitor, original );
            _root = _factory.CreateInstance<XRunnable>( _monitor, expanded, baseProvider );
            return _root != null;
        }

        public bool Run()
        {
            _issues.Clear();
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
            w.WriteLine( $"{_actions.Count} Actions:" );
            foreach( var a in _actions )
            {
                w.Write( " > " );
                w.WriteLine( a.ToString() );
            }
        }

        public IReadOnlyList<IIssue> Issues => _issues.Issues;

        public IReadOnlyList<XEnvAction> Actions => _actions;

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
