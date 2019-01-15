using CK.Core;
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
                Console.WriteLine();
                Console.WriteLine( "[run <globbed command name> | list [<globbed command name>] | restart | exit]" );
                Console.Write( $"{global.CurrentWorld.FullName}> " );
                string rep = Console.ReadLine().Trim();
                if( rep.Length == 0 )
                {
                    global.CommandRegister["World/Initialize"].Execute( monitor, null );
                    continue;
                }
                if( rep.StartsWith( "list" ) )
                {
                    rep = rep.Substring( 4 ).Trim();
                    Console.WriteLine( "Available Commands:" );
                    foreach( var c in global.CommandRegister.GetCommands( rep ).OrderBy( c => c.UniqueName ) )
                    {
                        Console.Write( "     " );
                        Console.WriteLine( c.UniqueName );
                    }
                    continue;
                }
                if( rep == "exit" ) return true;
                if( rep == "restart" )
                {
                    if( !global.Open() ) return false;
                    continue;
                }
                if( rep.StartsWith( "run " ) )
                {
                    rep = rep.Substring( 4 ).Trim();
                    var handlersByType = global.CommandRegister.GetCommands( rep )
                                            .GroupBy( c => c.PayloadType )
                                            .ToList();
                    if( handlersByType.Count == 0 )
                    {
                        monitor.Warn( $"Pattern '{rep}' has no match." );
                    }
                    else if( handlersByType.Count == 1 )
                    {
                        var firstHandler = handlersByType[0].First();
                        bool? globalConfirm = firstHandler.ConfirmationRequired;
                        if( handlersByType[0].Any( h => h.ConfirmationRequired != globalConfirm.Value ) )
                        {
                            monitor.Warn( "Different ConfirmationRequired found among commands. Each command that requires confirmation must be confirmed. " );
                            globalConfirm = null;
                        }
                        var payload = firstHandler.CreatePayload();
                        if( ReadPayload( monitor, payload ) )
                        {
                            char c = 'Y';
                            if( globalConfirm == true )
                            {
                                Console.WriteLine( "Confirm execution of:" );
                                foreach( var h in handlersByType[0] )
                                {
                                    Console.WriteLine( h.UniqueName );
                                }
                                DumpPayLoad( payload );
                                Console.WriteLine( "Y/N?" );
                                while( "YN".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) ;
                            }
                            if( c == 'Y' )
                            {
                                bool payloadDisplayed = false;
                                foreach( var h in handlersByType[0] )
                                {
                                    if( globalConfirm == null && h.ConfirmationRequired )
                                    {
                                        Console.Write( "Confirm execution of: " );
                                        Console.Write( h.UniqueName );
                                        if( payloadDisplayed )
                                        {
                                            Console.WriteLine( " (with same payload as before)" );
                                        }
                                        else 
                                        {
                                            payloadDisplayed = true;
                                            Console.WriteLine();
                                            DumpPayLoad( payload );
                                        }
                                        Console.WriteLine( "Y/N/C (to cancel all)?" );
                                        while( "YNC".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) ;
                                        if( c == 'N' ) continue;
                                        if( c == 'C' ) break;
                                    }
                                    h.Execute( monitor, payload );
                                }
                            }
                        }
                    }
                    else
                    {
                        using( monitor.OpenWarn( $"Pattern '{rep}' matches require {handlersByType.Count} different payload types." ) )
                        {
                            foreach( var c in handlersByType )
                            {
                                monitor.Warn( $"{c.Key?.Name ?? "<No payload>"}: {c.Select( n => n.UniqueName.ToString() ).Concatenate() }" );
                            }
                        }
                    }
                    continue;
                }
            }
        }

        static void DumpPayLoad( object payload )
        {
            if( payload != null )
            {
                Console.WriteLine( "With payload:" );
                if( payload is SimplePayload simple )
                {
                    foreach( var f in simple.Fields )
                    {
                        Console.WriteLine( f.ToString() );
                    }
                }
            }
        }

        static bool ReadPayload( ActivityMonitor monitor, object payload )
        {
            if( payload == null ) return true;
            if( !(payload is SimplePayload simple) )
            {
                monitor.Error( "Unsupported payload type: " + payload.GetType() );
                return false;
            }
            foreach( var f in simple.Fields )
            {
                Console.Write( $"{f.RequirementAndName}: " );
                if( f.Type == typeof( String ) )
                {
                    f.SetValue( f.IsPassword ? ReadSecret() : ReadNullableString() );
                }
                else if( f.Type == typeof( bool ) || f.Type == typeof( bool? ) )
                {
                    bool? a = ReadNullableBoolean();
                    if( a.HasValue ) f.SetValue( a.Value );
                    else if( !f.HasDefaultValue ) return false;
                }
                else if( f.Type == typeof( int ) )
                {
                    int? i = ReadPositiveNumber();
                    if( i.HasValue ) f.SetValue( i.Value );
                    else if( !f.HasDefaultValue ) return false;
                }
                else
                {
                    monitor.Error( "Unsupported field type: " + f.Type );
                    return false;
                }
            }
            return true;
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

        static bool IsEmptyString( string s ) => StringComparer.OrdinalIgnoreCase.Equals( s, "(empty)" );

        static string ReadNullableString()
        {
            string s = Console.ReadLine();
            if( s.Length == 0 ) return null;
            if( IsEmptyString( s ) ) return String.Empty;
            return s;
        }

        static string ReadSecret()
        {
            ConsoleColor fore = Console.ForegroundColor;
            ConsoleColor back = Console.BackgroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.BackgroundColor = ConsoleColor.Red;
            string s = ReadNullableString();
            Console.ForegroundColor = fore;
            Console.BackgroundColor = back;
            return s;
        }

        static bool? ReadNullableBoolean()
        {
            for( ; ;)
            {
                string s = ReadNullableString();
                if( s == null ) return null;
                if( StringComparer.OrdinalIgnoreCase.Equals( s, "true" ) ) return true;
                if( StringComparer.OrdinalIgnoreCase.Equals( s, "false" ) ) return false;
                Console.WriteLine( "Required 'true', 'false' or enter." );
            }
        }

        static int? ReadPositiveNumber()
        {
            string s = ReadNullableString();
            if( s == null ) return null;
            Match m = Regex.Match( s, @"\d+" );
            if( !m.Success ) return -1;
            return Int32.TryParse( m.Value, out int iss ) ? iss : -2;
        }


    }
}
