using CK.Core;
using CK.Env;
using CK.Monitoring;
using CK.SimpleKeyVault;

using NuGet.Protocol.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CKli
{
    class Program
    {
        static void Main( string[] args )
        {
            Console.WriteLine( "CKli " + CSemVer.InformationalVersion.ReadFromAssembly( Assembly.GetExecutingAssembly() ).ToString() );
            Console.WriteLine();

            ReadLine.HistoryEnabled = true;
            NormalizedPath userHostPath = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
            userHostPath = userHostPath.AppendPart( "CKli" );

            LogFile.RootLogPath = userHostPath.AppendPart( "Logs" );
            var logConfig = new GrandOutputConfiguration().AddHandler(
                                new CK.Monitoring.Handlers.TextFileConfiguration() { Path = "Text", MaxCountPerFile = 200_000} );
            GrandOutput.EnsureActiveDefault( logConfig );

            var monitor = new ActivityMonitor();
            monitor.Output.RegisterClient( new ColoredActivityMonitorConsoleClient() );
            monitor.MinimalFilter = LogFilter.Debug;

            MultipleWorldHome? multiHome = null;
            try
            {
                // We register the available XTypedObject once in a shared factory.
                var sharedTypedFactory = new XTypedFactory();
                Assembly.Load( "CKli.XObject" );
                sharedTypedFactory.AutoRegisterFromLoadedAssemblies( monitor );
                sharedTypedFactory.SetLocked();

                var mappings = new FileWorldLocalMapping( userHostPath.AppendPart( "WorldLocalMapping.txt" ) );
                var keyVault = new FileKeyVault( userHostPath.AppendPart( "Personal.KeyVault.txt" ) );

                OpenKeyVault( monitor, keyVault );

                Func<IReleaseVersionSelector> releaseVersionSelectorFactory = () => new ConsoleReleaseVersionSelector();

                var target = mappings.ReverseMap( Environment.CurrentDirectory );
                if( target != null )
                {
                    var home = SingleWorldHome.Create( monitor, userHostPath, sharedTypedFactory, target, keyVault.KeyStore, releaseVersionSelectorFactory );
                    if( home != null )
                    {
                        bool keepAlive = InteractiveRun( monitor, keyVault, null, home );
                        home.Dispose( monitor );
                        if( !keepAlive ) return;
                    }
                }
                multiHome = MultipleWorldHome.Create( monitor, userHostPath, sharedTypedFactory, keyVault, mappings, () => new ConsoleReleaseVersionSelector() );
                DumpWorlds( multiHome.WorldStore.ReadWorlds( monitor ) );
                InteractiveRun( monitor, keyVault, multiHome, null );
                multiHome.Dispose( monitor );
            }
            catch( Exception ex )
            {
                monitor.Fatal( ex );
                Console.WriteLine( "Hit a key." );
                Console.ReadKey();
            }
            GrandOutput.Default?.Dispose();
        }

        static bool InteractiveRun( IActivityMonitor monitor, FileKeyVault vault, MultipleWorldHome? multiHome, SingleWorldHome? home )
        {
            const string textCommands = "[run <globbed command name> | list [<globbed command name>] | cls | secret [clear NAME|set NAME|save] | exit]";
            const string textCommandsWithClose = "[run <globbed command name> | list [<globbed command name>] | close | cls | secret [clear NAME|set NAME|save] | exit]";
            CommandRegister commandRegister;
            if( multiHome != null )
            {
                commandRegister = multiHome.CommandRegister;
            }
            else
            {
                Debug.Assert( home != null );
                commandRegister = home.CommandRegister;
            }
            for(; ; )
            {
                var world = home?.World ?? multiHome?.WorldSelector.CurrentWorld;
                Console.WriteLine();
                if( world != null )
                {
                    Console.WriteLine( $"> World: {world.WorldName.FullName} - {textCommandsWithClose }" );
                }
                else
                {
                    Console.WriteLine( $"> {textCommands}" );
                }
                Console.Write( "> " );
                string rep = ReadLine.Read().Trim();
                if( rep.Length == 0 )
                {
                    if( world != null )
                    {
                        commandRegister["World/DumpWorldState"]!.Execute( monitor, null );
                    }
                    else if( multiHome != null )
                    {
                        DumpWorlds( multiHome.WorldStore.ReadWorlds( monitor ) );
                    }
                    continue;
                }
                if( rep == "exit" ) return false;
                if( rep == "cls" )
                {
                    Console.Clear();
                    continue;
                }
                if( rep == "close" )
                {
                    if( home != null ) return true;
                    Debug.Assert( multiHome != null );
                    commandRegister["World/CloseWorld"]!.Execute( monitor, null );
                    continue;
                }
                if( rep.StartsWith( "secret" ) )
                {
                    if( !vault.IsKeyVaultOpened )
                    {
                        Console.WriteLine( "Your personal KeyVault is not opened." );
                        OpenKeyVault( monitor, vault );
                    }
                    bool changedVault = false;
                    rep = rep.Substring( 6 ).Trim();
                    if( rep.Length > 0 )
                    {
                        var two = rep.Split( ' ' ).Where( t => t.Length > 0 ).ToArray();
                        if( two.Length == 1 )
                        {
                            if( two[0].Equals( "save", StringComparison.OrdinalIgnoreCase ) )
                            {
                                var s = ReadLine.ReadPassword( "Enter new passphrase (empty to cancel) [Hidden]: " );
                                if( s != null ) vault.SaveKeyVault( monitor, s );
                            }
                            else two = null;
                        }
                        else if( two.Length == 2 )
                        {
                            if( two[0].Equals( "clear", StringComparison.OrdinalIgnoreCase ) )
                            {
                                vault.UpdateSecret( monitor, two[1], null );
                            }
                            else if( two[0].Equals( "set", StringComparison.OrdinalIgnoreCase ) )
                            {
                                var s = ReadLine.ReadPassword( $"Enter '{two[1]}' secret (empty to cancel) [Hidden]: " );
                                if( s != null )
                                {
                                    changedVault = vault.UpdateSecret( monitor, two[1], s );
                                }
                            }
                            else two = null;
                        }
                        if( two == null )
                        {
                            Console.WriteLine( "Invalid syntax. Expected: 'clear NAME', 'set NAME' or 'save'." );
                        }
                    }
                    DumpSecrets( vault );
                    if( changedVault && multiHome != null ) multiHome.WorldStore.ReadWorlds( monitor );
                    continue;
                }
                if( rep.StartsWith( "list" ) )
                {
                    rep = rep.Substring( 4 ).Trim();
                    IEnumerable<ICommandHandler> commands;
                    if( rep.Length == 0 )
                    {
                        Console.Write( $"Available Commands:" );
                        commands = commandRegister.GetAllCommands();
                    }
                    else
                    {
                        Console.Write( $"Available Commands matching '{rep}':" );
                        commands = commandRegister.GetCommands( rep );
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
                    RunCommand( monitor, commandRegister, rep );
                    continue;
                }
                if( world == null && multiHome != null && Int32.TryParse( rep, out var idx ) )
                {
                    multiHome.WorldSelector.OpenWorld( monitor, idx.ToString() );
                    continue;
                }
                Console.WriteLine( "Unrecognized command." );
            }
        }

        static void OpenKeyVault( IActivityMonitor monitor, FileKeyVault vault )
        {
            Console.WriteLine();
            string prompt;
            if( vault.KeyVaultFileExists )
            {
                if( vault.OpenKeyVault( monitor ) )
                {
                    Console.WriteLine( "Your personal KeyVault is opened (it is not no protected)." );
                    return;
                }
                Console.WriteLine( "Your personal KeyVault should be opened." );
                prompt = "Enter the passphrase to open it [Hidden]: ";
            }
            else
            {
                Console.WriteLine( $"Personal KeyVault not found at: '{vault.KeyVaultPath}'" );
                Console.WriteLine( "It will contain encrypted secrets required by the different operations on all stacks." );
                prompt = "It should be created. Enter its passphrase and memorize it, or leave it empty to set it later [Hidden]: ";
            }
            var s = ReadLine.ReadPassword( prompt );
            if( string.IsNullOrWhiteSpace( s ) ) s = "CKli";
            vault.OpenKeyVault( monitor, s );

            if( vault.IsKeyVaultOpened )
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
            Console.WriteLine();
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
                ++idx;
                if( w.Root.IsEmptyPath )
                {
                    Console.WriteLine( $"     > ... {w.FullName} => (No local mapping. 'run World/SetWorldMapping' to set it.)" );
                }
                else
                {
                    Console.WriteLine( $"     > {idx} {w.FullName} => {(w.Root.IsEmptyPath ? "(No local mapping)" : w.Root.Path)}" );
                }
            }
        }

        static void DumpSecrets( FileKeyVault v )
        {
            static string FirstPadding( int paddingSize, string? sourceProviderName, bool missing )
            {
                bool provided = !string.IsNullOrWhiteSpace( sourceProviderName );
                if( missing && provided ) throw new InvalidOperationException( "Cannot be both missing and provided" );
                if( missing )
                {
                    sourceProviderName = "Missing";
                    provided = true;
                }

                return provided
                    ? $"[{sourceProviderName}]" + new string( ' ', paddingSize - sourceProviderName!.Length )
                    : new string( ' ', paddingSize + 2 );
            }

            static string PaddingByDepth( int depth ) => new string( ' ', depth * 5 );

            static void DoesThingWithGray( Action action )
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Gray;
                action();
                Console.ForegroundColor = prev;
            }

            static void WhitePipe() => DoesThingWithGray( () => Console.Write( "│" ) );

            static void RightArrow()
            {
                DoesThingWithGray( () => Console.Write( "└────┬> " ) );
            }

            int sourceNameMaxLength = "Required".Length;

            foreach( var k in v.KeyStore.Infos )
            {
                var sub = k;
                while( sub != null )
                {
                    if( !string.IsNullOrWhiteSpace( sub.SourceProviderName ) && sub.SourceProviderName.Length > sourceNameMaxLength )
                    {
                        sourceNameMaxLength = sub.SourceProviderName.Length;
                    }
                    sub = sub.SubKey;
                }
            }

            foreach( var k in v.KeyStore.Infos )
            {
                if( k.SuperKey != null ) continue;
                Console.ForegroundColor = k.IsSecretAvailable ? (!string.IsNullOrWhiteSpace( k.SourceProviderName ) ? ConsoleColor.Cyan : ConsoleColor.Green) : ConsoleColor.Red;
                Console.Write( FirstPadding( sourceNameMaxLength, k.SourceProviderName, !k.IsSecretAvailable ) );
                WhitePipe();
                Console.WriteLine( k.Name );
                Console.ForegroundColor = ConsoleColor.Gray;
                StringBuilder b = new StringBuilder();
                b.AppendMultiLine( FirstPadding( sourceNameMaxLength, null, false ) + "│", k.Description, true, false );
                b.AppendLine();
                Console.Write( b );
                var sub = k.SubKey;
                bool displayedAvailable = k.IsSecretAvailable;
                int depth = 0;
                while( sub != null )
                {
                    if( sub.IsSecretAvailable )
                    {
                        Console.ForegroundColor = displayedAvailable ? (!string.IsNullOrWhiteSpace( sub.SourceProviderName ) ? ConsoleColor.Cyan : ConsoleColor.DarkGreen) : ConsoleColor.Green;
                        displayedAvailable = true;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                    }
                    Console.Write( FirstPadding( sourceNameMaxLength, sub.SourceProviderName, !sub.IsSecretAvailable ) );
                    Console.Write( PaddingByDepth( depth ) );
                    RightArrow();
                    Console.WriteLine( sub.Name );
                    Console.ForegroundColor = ConsoleColor.Gray;
                    depth++;
                    b.Clear();
                    b.AppendMultiLine( FirstPadding( sourceNameMaxLength, sub.SourceProviderName, false ) + PaddingByDepth( depth ) + "│ ", sub.Description, true, false );
                    b.AppendLine();
                    Console.Write( b.ToString() );
                    sub = sub.SubKey;
                }
                Console.WriteLine( FirstPadding( sourceNameMaxLength, null, false ) );
            }
        }

        static void RunCommand( IActivityMonitor m, CommandRegister commandRegister, string rep )
        {
            var handlers = commandRegister.GetCommands( rep ).ToList();
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
            bool globalConfirm = firstHandler.ConfirmationRequired;
            ParallelCommandMode parallelMode = firstHandler.ParallelMode;

            if( handlers.Any( h => h.ConfirmationRequired != globalConfirm ) )
            {
                m.Error( "Different ConfirmationRequired found among commands. All commands must have the same ConfirmationRequired " );
                return;
            }
            if( handlers.Any( h => h.ParallelMode != parallelMode ) )
            {
                m.Error( "Different ParallelMode found among commands. All commands must have the same ParallelMode." );
                return;
            }
            var payload = firstHandler.CreatePayload();
            if( !ReadPayload( m, payload ) ) return;

            if( handlers.Count > 0 && parallelMode == ParallelCommandMode.UserChoice )
            {
                bool? a = YesNoCancel( "The selected commands can run in parallel. Do you want to run them in parallel?" );
                if( a == null ) return;
                parallelMode = a.Value ? ParallelCommandMode.Parallel : ParallelCommandMode.Sequential;
                globalConfirm = false;
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
            if( parallelMode == ParallelCommandMode.Sequential )
            {
                foreach( var h in handlers )
                {
                    // Stops the loop at the first error.
                    if( h.Execute( m, payload ) != null ) break;
                }
            }
            else
            {
                // Using parallel execution, it's not so easy to "stop at the first" error:
                // we let the commands be executed.
                var tasks = handlers.Select( h =>
                {
                    var token = m.CreateDependentToken();
                    return Task.Run( () =>
                    {
                        ActivityMonitor monitor = new ActivityMonitor();
                        using( monitor.StartDependentActivity( token ) )
                        {
                            h.Execute( monitor, payload );
                        }
                    } );
                } ).ToArray();
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

        static bool? YesNoCancel( string message )
        {
            char c;
            Console.WriteLine( message );
            Console.WriteLine( "Yes, No or Cancel? (Y/N/C)" );
            while( "YNCync".IndexOf( (c = Console.ReadKey().KeyChar) ) < 0 ) Console.Write( '\b' );
            if( c == 'C' || c == 'c' ) return null;
            return c == 'Y' || c == 'y';
        }

        static void DumpPayLoad( object? payload )
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

        static bool ReadPayload( IActivityMonitor monitor, object? payload )
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
                    if( f.IsPassword ) f.SetValue( ReadLine.ReadPassword() );
                    else
                    {
                        var s = ReadNullableString();
                        if( !f.HasDefaultValue || s != null ) f.SetValue( s );
                    }
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
                        Console.WriteLine( $"    {Convert.ChangeType( enumValue, Enum.GetUnderlyingType( f.Type ) )}:{Enum.GetName( f.Type, enumValue! )}" );
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
            string s = ReadLine.Read();
            if( s.Length == 0 ) return null;
            if( IsEmptyString( s ) ) return String.Empty;
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
