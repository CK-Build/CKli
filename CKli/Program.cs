using CK.Core;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using CK.Env;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using CK.Text;
using CK.Monitoring;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

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
            var goc = new GrandOutputConfiguration();
            goc.Handlers.Add( new CK.Monitoring.Handlers.ConsoleConfiguration() );
            GrandOutput.EnsureActiveDefault( goc );
            var monitor = new ActivityMonitor();
            var xFactory = new XTypedFactory();
            xFactory.AutoRegisterFromLoadedAssemblies( monitor );
            IBasicApplicationLifetime appLife = new FakeApplicationLifetime();
            var rootPath = GetRootPath( args );
            using( var global = new GlobalContext( monitor, xFactory, rootPath, appLife ) )
            {
                if( !InteractiveRun( monitor, global, appLife ) ) Console.ReadLine();
            }
        }

        static bool InteractiveRun( ActivityMonitor monitor, GlobalContext global, IBasicApplicationLifetime appLife )
        {
            if( !global.Open() ) return false;
            for(; ; )
            {
                if( appLife.CanCancelStopRequest ) appLife.CancelStopRequest();
                Console.WriteLine();
                Console.WriteLine( $"> World: {global.CurrentWorld.FullName} - [run <globbed command name> | list [<globbed command name>] | cls | restart | exit]" );
                Console.Write( "> " );
                string rep = Console.ReadLine().Trim();
                if( rep.Length == 0 )
                {
                    global.CommandRegister["World/DumpWorldState"].Execute( monitor, null );
                    continue;
                }
                if( rep == "cls" )
                {
                    Console.Clear();
                    continue;
                }
                if( rep.StartsWith( "list" ) )
                {
                    rep = rep.Substring( 4 ).Trim();
                    IEnumerable<ICommandHandler> commands;
                    if( rep.Length == 0 )
                    {
                        Console.Write( $"Available Commands:" );
                        commands = global.CommandRegister.GetAllCommands();
                    }
                    else
                    {
                        Console.Write( $"Available Commands matching '{rep}':" );
                        commands = global.CommandRegister.GetCommands( rep );
                    }

                    bool atLeastOne = false;
                    foreach( var c in commands.OrderBy( c => c.UniqueName ) )
                    {
                        if( !atLeastOne )
                        {
                            Console.WriteLine();
                            atLeastOne = true;
                        }
                        Console.Write( "     " );
                        Console.WriteLine( c.UniqueName );
                    }
                    if( !atLeastOne ) Console.WriteLine( " (none)" );
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
                    RunCommand( monitor, global, appLife, rep );
                    continue;
                }
                Console.WriteLine( "Unrecognized command." );
            }
        }

        static void RunCommand( ActivityMonitor m, GlobalContext global, IBasicApplicationLifetime appLife, string rep )
        {
            var handlers = global.CommandRegister.GetCommands( rep );
            var handlersBySig = handlers
                                    .GroupBy( c => c.PayloadSignature )
                                    .ToList();
            if( handlersBySig.Count == 0 )
            {
                m.Warn( $"Pattern '{rep}' has no match." );
                return;
            }
            if( handlersBySig.Count != 1 )
            {
                using( m.OpenWarn( $"Pattern '{rep}' matches require {handlersBySig.Count} different payloads." ) )
                {
                    foreach( var c in handlersBySig )
                    {
                        m.Warn( $"{c.Key ?? "<No payload>"}: {c.Select( n => n.UniqueName.ToString() ).Concatenate() }" );
                    }
                }
                return;
            }
            var firstHandler = handlers.First();
            bool? parallelRun = firstHandler.ParallelRun;
            bool? globalConfirm = firstHandler.ConfirmationRequired;
            bool? backgroundRun = firstHandler.BackgroundRun;

            if( handlers.Any( h => h.ConfirmationRequired != globalConfirm.Value ) )
            {
                m.Error( "Different ConfirmationRequired found among commands. All commands must have the same ConfirmationRequired " );
                return;
            }
            if( handlers.Any( h => h.BackgroundRun != backgroundRun ) )
            {
                m.Error( "Different BackgroundCommandAttribute found among commands. All commands must have the same attributes and identically configured attribute." );
                return;
            }
            if( handlers.Any( h => h.ParallelRun != parallelRun ) )
            {
                m.Error( "Different ParallelCommandAttribute found among commands. All commands must have the same attributes and identically configured attribute." );
                return;
            }
            var payload = firstHandler.CreatePayload();
            if( !ReadPayload( m, payload ) ) return;

            if( parallelRun == null )
            {
                Console.WriteLine( "The selected command(s) can be run in parallel. Do you want to run them in parallel ?" );
                parallelRun = YesOrNo();
            }

            if( backgroundRun == null )
            {
                Console.WriteLine( "The selected command(s) can be run in background. Do you want to run them in background ?" );
                backgroundRun = YesOrNo();
            }

            if( globalConfirm == true )
            {
                Console.WriteLine( "Confirm execution of:" );
                foreach( var h in handlers )
                {
                    Console.WriteLine( h.UniqueName );
                }
                DumpPayLoad( payload );
                if( !YesOrNo() ) return;
            }
            if( backgroundRun.Value )
            {
                throw new NotImplementedException();
            }

            if( !parallelRun.Value )
            {

                foreach( var h in handlers )
                {
                    if( appLife.StopRequested( m ) ) break;
                    h.Execute( m, payload );
                }

            }
            else
            {
                var tasks = handlers.Select( h => Task.Run( () =>
                {
                    ActivityMonitor monitor = new ActivityMonitor();
                    h.Execute( monitor, payload );
                } ) ).ToArray();
                Task.WaitAll( tasks );
            }
            
        }


        static bool YesOrNo()
        {
            char c;
            Console.WriteLine( "Y/N?" );
            while( "YN".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) ;
            return c == 'Y';
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
                else if( f.Type == typeof( LogFilter ) )
                {
                    Console.WriteLine( "Enter number (or enter to cancel):" );
                    Console.WriteLine( "   0 - Undefined, 1 - Debug, 2 - Trace, 3 - Verbose, 4 - Monitor, 5 - Terse, 6 - Release" );
                    int? i = ReadPositiveNumber();
                    if( i.HasValue )
                    {
                        switch( i.Value )
                        {
                            case 0: f.SetValue( LogFilter.Undefined ); break;
                            case 1: f.SetValue( LogFilter.Debug ); break;
                            case 2: f.SetValue( LogFilter.Trace ); break;
                            case 3: f.SetValue( LogFilter.Verbose ); break;
                            case 4: f.SetValue( LogFilter.Monitor ); break;
                            case 5: f.SetValue( LogFilter.Terse ); break;
                            case 6: f.SetValue( LogFilter.Release ); break;
                            default:
                                monitor.Error( $"Invalid choice." );
                                return false;
                        }
                    }
                    else if( !f.HasDefaultValue ) return false;
                }
                else if( f.Type.IsEnum )
                {
                    bool isFlags = f.Type.GetCustomAttributes( typeof( FlagsAttribute ), false ).Length > 0;
                    Console.WriteLine( isFlags ? "Combinable flags:" : "Values: " );
                    foreach( var enumValue in Enum.GetValues( f.Type ) )
                    {
                        Console.WriteLine( $"    {Convert.ChangeType( enumValue, Enum.GetUnderlyingType( f.Type ) )}:{Enum.GetName( f.Type, enumValue )}" );
                    }
                    var s = ReadNullableString();
                    if( s == null )
                    {
                        return f.HasDefaultValue;
                    }
                    if( Enum.TryParse( f.Type, s, out var value ) )
                    {
                        f.SetValue( value );
                    }
                    else
                    {
                        monitor.Error( $"Unable to parse: '{s}' as {f.Type.Name} enum." );
                    }
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
                            .Select( m => int.Parse( m.Value ) )
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
            for(; ; )
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
