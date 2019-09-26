using CK.Core;
using System;
using System.Linq;
using CK.Env;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using CK.Text;
using CK.Monitoring;
using System.Threading.Tasks;
using System.Text;

namespace CKli
{
    class Program
    {
        static void Main( string[] args )
        {
            NormalizedPath userHostPath = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
            userHostPath = userHostPath.AppendPart( "CKli" );

            LogFile.RootLogPath = userHostPath.AppendPart( "Logs" );
            var logConfig = new GrandOutputConfiguration().AddHandler(
                                new CK.Monitoring.Handlers.TextFileConfiguration() { Path = "Text" } );
            GrandOutput.EnsureActiveDefault( logConfig );

            var monitor = new ActivityMonitor();
            monitor.Output.RegisterClient( new ColoredActivityMonitorConsoleClient() );
            monitor.MinimalFilter = LogFilter.Debug;

            IBasicApplicationLifetime appLife = new FakeApplicationLifetime();
            try
            {
                using( var host = new UserHost( appLife, userHostPath ) )
                {
                    host.Initialize( monitor );
                    OpenKeyVault( monitor, host );
                    if( host.UserKeyVault.IsKeyVaultOpened ) host.Initialize( monitor );
                    DumpWorlds( host.WorldStore.ReadWorlds( monitor ) );
                    InteractiveRun( monitor, host );
                }
            }
            catch( Exception ex )
            {
                monitor.Fatal( ex );
                Console.ReadLine();
            }
        }

        static void InteractiveRun( IActivityMonitor monitor, UserHost host )
        {
            const string textCommands = "[run <globbed command name> | list [<globbed command name>] | cls | secret [clear NAME|set NAME|save] | exit]";
            for(; ; )
            {
                if( host.ApplicationLifetime.CanCancelStopRequest ) host.ApplicationLifetime.CancelStopRequest();

                bool hasWorld = host.WorldSelector.CurrentWorld != null;
                Console.WriteLine();
                if( hasWorld )
                {
                    Console.WriteLine( $"> World: {host.WorldSelector.CurrentWorld.WorldName.FullName} - {textCommands }" );
                }
                else
                {
                    Console.WriteLine( $"> {textCommands}" );
                }
                Console.Write( "> " );
                string rep = Console.ReadLine().Trim();
                if( rep.Length == 0 )
                {
                    if( hasWorld ) host.CommandRegister["World/DumpWorldState"].Execute( monitor, null );
                    else DumpWorlds( host.WorldStore.ReadWorlds( monitor ) );
                    continue;
                }
                if( rep == "cls" )
                {
                    Console.Clear();
                    continue;
                }
                if( rep.StartsWith( "secret" ) )
                {
                    if( !host.UserKeyVault.IsKeyVaultOpened )
                    {
                        Console.WriteLine( "Your personal KeyVault is not opened." );
                        OpenKeyVault( monitor, host );
                    }
                    rep = rep.Substring( 6 ).Trim();
                    if( rep.Length > 0 )
                    {
                        var two = rep.Split( ' ' ).Where( t => t.Length > 0 ).ToArray();
                        if( two.Length == 1 )
                        {
                            if( two[0].Equals( "save", StringComparison.OrdinalIgnoreCase ) )
                            {
                                Console.Write( $"Enter new passphrase (empty to cancel): " );
                                var s = ReadSecret();
                                if( s != null ) host.UserKeyVault.SaveKeyVault( monitor, s );
                            }
                            else two = null;
                        }
                        else if( two.Length == 2 )
                        {
                            if( two[0].Equals( "clear", StringComparison.OrdinalIgnoreCase ) )
                            {
                                host.UserKeyVault.UpdateSecret( monitor, two[1], null );
                            }
                            else if( two[0].Equals( "set", StringComparison.OrdinalIgnoreCase ) )
                            {
                                Console.Write( $"Enter '{two[1]}' secret (empty to cancel): " );
                                var s = ReadSecret();
                                if( s != null ) host.UserKeyVault.UpdateSecret( monitor, two[1], s );
                            }
                            else two = null;
                        }
                        if( two == null )
                        {
                            Console.WriteLine( "Invalid syntax. Expected: 'clear NAME', 'set NAME' or 'save'." );
                        }
                    }
                    DumpSecrets( host.UserKeyVault );
                    continue;
                }
                if( rep.StartsWith( "list" ) )
                {
                    rep = rep.Substring( 4 ).Trim();
                    IEnumerable<ICommandHandler> commands;
                    if( rep.Length == 0 )
                    {
                        Console.Write( $"Available Commands:" );
                        commands = host.CommandRegister.GetAllCommands();
                    }
                    else
                    {
                        Console.Write( $"Available Commands matching '{rep}':" );
                        commands = host.CommandRegister.GetCommands( rep );
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
                if( rep.StartsWith( "run " ) )
                {
                    rep = rep.Substring( 4 ).Trim();
                    RunCommand( monitor, host, rep );
                    continue;
                }
                if( rep == "exit" ) return;
                if( !hasWorld && Int32.TryParse( rep, out var idx ) )
                {
                    host.WorldSelector.Open( monitor, idx.ToString() );
                    continue;
                }
                Console.WriteLine( "Unrecognized command." );
            }
        }

        static void OpenKeyVault( IActivityMonitor monitor, UserHost host )
        {
            string prompt;
            if( host.UserKeyVault.KeyVaultFileExists )
            {
                Console.WriteLine( "Your personal KeyVault should be opened." );
                prompt = "Enter the passphrase to open it: ";
            }
            else
            {
                Console.WriteLine( $"Personal KeyVault not found at: '{host.UserKeyVault.KeyVaultPath}'" );
                Console.WriteLine( "It will contain encrypted secrets required by the different operations on all stacks." );
                prompt = "It should be created. Enter its passphrase (and memorize it!): ";
            }

            Console.Write( prompt );
            var s = ReadSecret();
            if( s != null ) host.UserKeyVault.OpenKeyVault( monitor, s );
            else Console.WriteLine( "KeyVault opening cancelled." );

            if( host.UserKeyVault.IsKeyVaultOpened )
            {
                Console.WriteLine( "KeyVault opened." );
            }
            else
            {
                Console.WriteLine( "KeyVault not opened. You may open it later by using the 'secret' command." );
                Console.WriteLine( "Some features will be unavailable or fail miserably when secrets are unavailable." );
            }
            Console.WriteLine( "Note:" );
            Console.WriteLine( " - You can always use 'secret set NAME' or 'secret clear NAME' commands at anytime to upsert or remove secrets." );
            Console.WriteLine( " - You can also change the KeyVault passphrase thanks to 'secret save'." );
        }

        static void DumpWorlds( IEnumerable<IRootedWorldName> worlds )
        {
            Console.WriteLine( "--------- Worlds ---------" );
            string name = null;
            int idx = 0;
            foreach( var w in worlds )
            {
                if( name != w.Name )
                {
                    Console.WriteLine( $"  - {w.Name}" );
                    name = w.Name;
                }
                Console.WriteLine( $"     > {++idx} {w.FullName} => {(w.Root.IsEmptyPath ? "(No local mapping)" : w.Root.Path)}" );
            }
        }

        static void DumpSecrets( UserKeyVault v )
        {
            string FirstPadding( bool missing )
            {
                return missing ? "[Missing]" : "         ";
            }

            string PaddingByDepth( int depth ) => new string( ' ', depth * 5 );

            void DoesThingWithGray( Action action )
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Gray;
                action();
                Console.ForegroundColor = prev;
            }

            void WhitePipe() => DoesThingWithGray( () => Console.Write( "│" ) );
            void RightArrow()
            {
                DoesThingWithGray( () => Console.Write( "└────┬> " ) );
            }

            foreach( var k in v.KeyStore.Infos )
            {
                if( k.SuperKey != null ) continue;
                Console.ForegroundColor = k.IsSecretAvailable ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write( FirstPadding( !k.IsSecretAvailable ) );
                WhitePipe();
                Console.WriteLine( k.Name );
                Console.ForegroundColor = ConsoleColor.Gray;
                StringBuilder b = new StringBuilder();
                b.AppendMultiLine( FirstPadding( false ) + "│", k.Description, true, false );
                b.AppendLine();
                Console.Write( b );
                var sub = k.SubKey;
                bool displayedAvailable = k.IsSecretAvailable;
                int depth = 0;
                while( sub != null )
                {
                    if( sub.IsSecretAvailable )
                    {
                        Console.ForegroundColor = displayedAvailable ? ConsoleColor.DarkGreen : ConsoleColor.Green;
                        displayedAvailable = true;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                    }
                    Console.Write( FirstPadding( !sub.IsSecretAvailable ) );
                    Console.Write( PaddingByDepth( depth ) );
                    RightArrow();
                    Console.WriteLine( sub.Name );
                    Console.ForegroundColor = ConsoleColor.Gray;
                    depth++;
                    b.Clear();
                    b.AppendMultiLine( FirstPadding( false ) + PaddingByDepth( depth ) + "│ ", sub.Description, true, false );
                    b.AppendLine();
                    Console.Write( b.ToString() );
                    sub = sub.SubKey;
                }
                Console.WriteLine( FirstPadding( false ) );
            }
        }



        static void RunCommand( IActivityMonitor m, UserHost host, string rep )
        {
            var handlers = host.CommandRegister.GetCommands( rep ).ToList();
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
            var firstHandler = handlers[0];
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
                Console.WriteLine( "The selected command(s) can run in parallel. Do you want to run in parallel ?" );
                parallelRun = YesOrNo();
            }

            if( backgroundRun == null )
            {
                Console.WriteLine( "The selected command(s) can run in background. Do you want to run in background ?" );
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
                    if( host.ApplicationLifetime.StopRequested( m ) ) break;
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
            while( "YNyn".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) Console.Write( '\b' );
            return c == 'Y' || c == 'y';
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

        static bool ReadPayload( IActivityMonitor monitor, object payload )
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
                else if( f.Type == typeof( NormalizedPath ) )
                {
                    Console.Write( "Enter path: " );
                    f.SetValue( new NormalizedPath( ReadNullableString() ) );
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
