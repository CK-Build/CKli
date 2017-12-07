using CK.Core;
using CK.Env.Analysis;

using LibGit2Sharp;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Linq;
using CK.Env;

namespace CKli
{
    class Program
    {
        static string GetThisFilePath( [CallerFilePath]string p = null ) => p;

        static string GetRootPath( string[] args )
        {
            if( args.Length > 0 )
            {
                return args[0];
            }
            var p = GetThisFilePath();
            while( !String.IsNullOrEmpty( p ) && Path.GetFileName( p ) != "CKli" ) p = Path.GetDirectoryName( p );
            if( !String.IsNullOrEmpty( p ) )
            {
                var ckEnv = Path.GetDirectoryName( p );
                if( Path.GetFileName( ckEnv ) == "CK-Env" )
                {
                    return Path.GetDirectoryName( ckEnv );
                }
            }
            throw new InvalidOperationException( "Must be in CK-Env/CKli source." );
        }

        static void Main( string[] args )
        {
            ActivityMonitor.DefaultFilter = LogFilter.Debug;
            var monitor = new ActivityMonitor();
            monitor.Output.RegisterClient( new ActivityMonitorConsoleClient() );
            var xFactory = new XTypedFactory();
            xFactory.AutoRegisterFromLoadedAssemblies();

            var rootPath = GetRootPath( args );
            using( var global = new GlobalContext( monitor, xFactory, rootPath ) )
            {
                if( global.Open() )
                {
                    for(; ; )
                    {
                        global.DisplayIssues( Console.Out );
                        Console.WriteLine( $"exit | r[estart] | d[esc] #issue | f[ix] #issue" );
                        Console.Write( $">" );
                        string rep;
                        while( (rep = Console.ReadLine().Trim()).Length == 0 );
                        if( rep[0] == 'e' ) break;
                        if( rep[0] == 'r' )
                        {
                            if( !global.Open() ) break;
                            continue;
                        }
                        if( rep[0] == 'd' )
                        {
                            int iss = GetIssueNumber( rep );
                            if( iss < 0 || iss > global.I)
                        }
                    }
                }
            }

            var fs = new FileSystem( rootPath );
            var issues = new IssueCollector();
            var baseProvider = new SimpleServiceContainer();
            baseProvider.Add<ISimpleObjectActivator>( new SimpleObjectActivator() );
            baseProvider.Add( fs );
            baseProvider.Add( issues );

            var knownWorldPath = Path.Combine( rootPath, "CK-Env", "KnownWorld.xml" );
            var w = xFactory.CreateInstance<XRunnable>( monitor, XDocument.Load( knownWorldPath ).Root, baseProvider );
            if( w != null )
            {
                var runContext = new XRunnable.DefaultContext( monitor );
                if( !w.Run( runContext ) )
                {
                    monitor.Error( "Failed Run." );
                }
                else
                {
                    int fixCount = 0;
                    foreach( var i in issues.Issues )
                    {
                        if( i.HasAutoFix ) ++fixCount;
                        Console.WriteLine( $"{i.Number} {(i.HasAutoFix ? "*" : " ")} - {i.MaxLevel} - {i.Title}" );
                    }
                    if( fixCount > 0 )
                    {
                        Console.WriteLine( $"{fixCount} automatic fixes available." );
                    }
                    Console.WriteLine( $"exit | r[un] | d[esc] #issue | f[ix] #issue" );
                    Console.Write( $">" );
                }
            }

            Console.ReadLine();
        }
    }
}
