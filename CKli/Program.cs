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
using CK.Text;

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
            Console.OutputEncoding = System.Text.Encoding.Unicode;
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
                Console.Write( $">" );
                string rep = Console.ReadLine().Trim();
                if( rep.Length == 0 )
                {
                    foreach( var c in global.CommandRegister.GetAll().OrderBy( c => c.UniqueName ) )
                    {
                        Console.WriteLine( c.UniqueName );
                    }
                    continue;
                }
                if( rep == "exit" ) return true;
                if( rep == "refresh" )
                {
                    if( !global.Open() ) return false;
                    continue;
                }
                if( rep.StartsWith( "run " ) )
                {
                    rep = rep.Substring( 4 ).Trim();
                    var handlersByType = global.CommandRegister.Select( rep )
                                            .GroupBy( c => c.PayloadType )
                                            .ToList();
                    if( handlersByType.Count == 0 )
                    {
                        monitor.Warn( $"Pattern '{rep}' has no match." );
                    }
                    else if( handlersByType.Count == 1 )
                    {
                        var payload = handlersByType[0].First().CreatePayload();
                        foreach( var c in handlersByType[0] )
                        {
                            c.Execute( monitor, payload );
                        }
                    }
                    else
                    {
                        using( monitor.OpenWarn( $"Pattern '{rep}' matches require {handlersByType.Count} different payload types." ) )
                        {
                            foreach( var c in handlersByType )
                            {
                                monitor.OpenWarn( $"{c.Key?.Name ?? "<No payload>"}: {c.Select( n => n.UniqueName.ToString() ).Concatenate() }" );
                            }
                        }
                    }
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
                if( rep.Contains( " all" ) ) return Enumerable.Range( min, max - min + 1 ).ToArray();
                return Regex.Matches( rep, @"\d+" )
                            .Cast<Match>()
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
