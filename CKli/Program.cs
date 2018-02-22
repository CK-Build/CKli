using CK.Core;
using CK.Env.Analysis;

using LibGit2Sharp;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Linq;
using CK.Env;
using System.Text.RegularExpressions;
using System.Collections.Generic;

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
                if( !InteractiveRun( monitor, global ) ) Console.ReadLine();
            }
        }

        static bool InteractiveRun( ActivityMonitor monitor, GlobalContext global )
        {
            if( !global.Open() ) return false;
            for(; ; )
            {
                if( !global.Run() ) return false;
                global.DisplayIssues( Console.Out, true );
                global.DisplayActions( Console.Out );
                Console.WriteLine( $"e[xit] | r[estart] | f[ix] #issue | a[ction] #action" );
                Console.Write( $">" );
                string rep = Console.ReadLine().Trim();
                if( rep.Length == 0 ) continue;
                if( rep[0] == 'e' ) return true;
                if( rep[0] == 'r' )
                {
                    if( !global.Open() ) return false;
                    continue;
                }
                if( rep[0] == 'a' )
                {
                    int act = ReadNumber( rep );
                    if( act < 0 || act >= global.Actions.Count )
                    {
                        Console.WriteLine( $"Invalid action number." );
                    }
                    else
                    {
                        global.RunAction( monitor, act );
                    }
                    Console.WriteLine( "<<Hit a key>>" );
                    Console.ReadKey();
                    continue;
                }
                if( rep[0] == 'f' )
                {
                    var issues = ReadNumbers( monitor, rep, 0, global.Issues.Count - 1 );
                    if( issues.Count > 0 )
                    {
                        foreach( int iss in issues )
                        {
                            if( !global.Issues[iss].AutoFix( monitor ) ) break;
                        }
                    }
                    Console.WriteLine( "<<Hit a key>>" );
                    Console.ReadKey();
                    continue;
                }
            }
        }

        static int ReadNumber( string rep )
        {
            Match m = Regex.Match( rep, @"\d+" );
            if( !m.Success ) return -1;
            return Int32.TryParse( m.Value, out int iss ) ? iss : -2;
        }

        static IReadOnlyList<int> ReadNumbers( IActivityMonitor monitor, string rep, int min, int max )
        {
            try
            {
                return Regex.Matches( rep, @"\d+" )
                            .Select( m => Int32.Parse( m.Value ) )
                            .Where( v => v >= min && v <= max )
                            .ToList();
            }
            catch( Exception ex )
            {
                monitor.Error( ex );
                return Array.Empty<int>();
            }
        }
    }
}
