using CK.Core;
using CK.Monitoring;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// The root environment is in charge of the <see cref="AppLocalDataPath"/> and to provide a <see cref="ISecretsStore"/>.
/// It may be extended in the future to handle other basic locally configurable aspect but currently it has all what we need.
/// This must be initialized before anything can be done with the <see cref="StackRepository"/>.
/// <para>
/// This is a static class. Tests use the <see cref="Initialize(string?, CommandLineArguments?, IScreen?)"/> instance name to isolate the
/// test environment ("CKli-Test") from the regular run environment ("CKli").
/// </para>
/// <para>
/// This captures the initial <see cref="Environment.CurrentDirectory"/> and initializes the <see cref="GrandOutput.Default"/> if it is
/// not already initialized.
/// </para>
/// </summary>
public static partial class CKliRootEnv
{
    static NormalizedPath _appLocalDataPath;
    static ISecretsStore? _secretsStore;
    static IScreen? _screen;
    static NormalizedPath _currentDirectory;
    static NormalizedPath _currentStackPath;
    static CKliEnv? _defaultCommandContext;
    static bool? _shouldDeletePureCKliLogFile;

    /// <summary>
    /// Initializes the CKli environment.
    /// </summary>
    /// <param name="instanceName">Used by tests (with "Test"). Can be used with other suffix if needed.</param>
    /// <param name="arguments">Optional arguments. when provided, this handles the <c>--screen</c> option.</param>
    /// <param name="screen">Optional <see cref="StringScreen"/> to use or <see cref="NoScreen"/>.</param>
    public static void Initialize( string? instanceName = null, CommandLineArguments? arguments = null, IScreen? screen = null )
    {
        Throw.CheckState( "Initialize can be called only once.", _appLocalDataPath.IsEmptyPath );
        _appLocalDataPath = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create ),
                                          instanceName == null ? "CKli" : $"CKli-{instanceName}" );
        // To handle logs, we first must determine if we are in a Stack. If this is the case, then the Logs/ folder
        // will be .[Public|PrivateStack]/Logs, else the log will be in _appLocalDataPath/Out-of-Stack-Logs/.
        _currentDirectory = Environment.CurrentDirectory;
        _currentStackPath = StackRepository.FindGitStackPath( _currentDirectory );

        _screen = screen ?? CreateScreen( arguments );

        InitializeMonitoring( _currentDirectory, _currentStackPath );
        NormalizedPath configFilePath = GetConfigPath();
        try
        {
            if( !File.Exists( configFilePath ) )
            {
                SetAndWriteDefaultConfig();
            }
            else
            {
                var lines = File.ReadAllLines( configFilePath );
                if( lines.Length == 1 )
                {
                    var secretsStoreTypeName = lines[0];
                    var secretsStoreType = Type.GetType( secretsStoreTypeName, throwOnError: false );
                    if( secretsStoreType == null )
                    {
                        Console.WriteLine( $"Unable to locate type '{secretsStoreTypeName}'. Using default DotNetUserSecretsStore." );
                        secretsStoreType = typeof( DotNetUserSecretsStore );
                    }
                    _secretsStore = (ISecretsStore)Activator.CreateInstance( secretsStoreType )!;
                }
                else
                {
                    _screen.ScreenLog( LogLevel.Warn, $"""
                    Invalid '{configFilePath}':
                    {lines.Concatenate( Environment.NewLine )}
                    Resetting it to default values.
                    """ );
                    SetAndWriteDefaultConfig();
                }
            }
        }
        catch( DirectoryNotFoundException )
        {
            Directory.CreateDirectory( _appLocalDataPath );
            SetAndWriteDefaultConfig();
        }
        catch( Exception ex )
        {
            _screen.ScreenLog( LogLevel.Warn, $"""
                Error while initializing CKliRootEnv:
                {CKExceptionData.CreateFrom( ex )}
                Resetting the '{configFilePath}' to default values.
                """ );
            SetAndWriteDefaultConfig();
        }

        _defaultCommandContext = new CKliEnv( _screen, _secretsStore, _currentDirectory, _currentStackPath, DateTimeOffset.MinValue );

        [MemberNotNull( nameof( _secretsStore ) )]
        static void SetAndWriteDefaultConfig()
        {
            _secretsStore = new DotNetUserSecretsStore();
            WriteConfiguration( null );
        }

        static void InitializeMonitoring( NormalizedPath currentDirectory, NormalizedPath currentStackPath )
        {
            // If the logging is already configured, we do nothing (except logging this first initialization).
            if( GrandOutput.Default == null )
            {
                if( LogFile.RootLogPath == null )
                {
                    LogFile.RootLogPath = currentStackPath.IsEmptyPath
                                            ? CKliRootEnv.AppLocalDataPath.AppendPart( "Out-of-Stack-Logs" )
                                            : currentStackPath.AppendPart( "Logs" );
                }
                ActivityMonitor.DefaultFilter = LogFilter.Diagnostic;
                GrandOutput.EnsureActiveDefault( new GrandOutputConfiguration()
                {
                    Handlers = { new CK.Monitoring.Handlers.TextFileConfiguration { Path = "Text", MaximumTotalKbToKeep = 2 * 1024 /*2 MBytes */ } }
                } );
            }
            else
            {
                ActivityMonitor.StaticLogger.Info( $"""
                        Initializing CKliRootEnv:
                        CurrentDirectory = '{currentDirectory}'
                        AppLocalDataPath = '{CKliRootEnv.AppLocalDataPath}'
                        """ );
            }
        }

        static IScreen CreateScreen( CommandLineArguments? arguments )
        {
            bool forceAnsi = false;
            var optScreen = arguments?.ScreenOption;
            if( optScreen != null )
            {
                if( optScreen.Equals( "no-color", StringComparison.OrdinalIgnoreCase )
                    || optScreen.Equals( "no_color", StringComparison.OrdinalIgnoreCase ) )
                {
                    return new ConsoleScreen();
                }
                if( optScreen.Equals( "none", StringComparison.OrdinalIgnoreCase ) )
                {
                    return new NoScreen();
                }
                forceAnsi = optScreen.Equals( "force-ansi", StringComparison.OrdinalIgnoreCase );
            }
            // Applying https://no-color.org/
            bool ansiConsole = forceAnsi || string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "NO_COLOR" ) );
            uint? originalConsoleMode = null;
            if( ansiConsole )
            {
                (ansiConsole, originalConsoleMode) = AnsiDetector.TryEnableAnsiColorCodes();
            }
            IScreen s = forceAnsi || ansiConsole ? new AnsiScreen( originalConsoleMode ) : new ConsoleScreen();
            return s;
        }
    }

    /// <summary>
    /// Should be called before leaving the application.
    /// This dispose the GrandOutput.Default and removes 'ckli log' or 'ckli -?' text
    /// log files.
    /// </summary>
    public static async Task CloseAsync( IActivityMonitor monitor, CommandLineArguments arguments )
    {
        CheckInitialized();
        _screen.Close();
        if( _secretsStore is IDisposable d ) d.Dispose();
        var defaultOutput = GrandOutput.Default;
        if( defaultOutput != null )
        {
            // Before disposing, posts the suppression of the current log file if it
            // must be suppressed and preventely removes the handler.
            var logCloser = new TextLogFileCloser( ShouldDeleteCurrentLogFile( arguments ), deactivateHandler: true );
            defaultOutput.Sink.Submit( logCloser );
            await logCloser.Completion.ConfigureAwait( false );
            await defaultOutput.DisposeAsync().ConfigureAwait( false );
        }
    }

    static bool ShouldDeleteCurrentLogFile( CommandLineArguments arguments )
    {
        // We have a choice here:
        // - By checking is true, we remove the log file if ONLY 'ckli log' has been called.
        // - By checking is not false (ie. true or null), we remove a log file when no command
        //   has been executed: calls to help or to command with argument errors are not logged.
        //
        // Currently we chose to forget help only, keeping a log with command argument errors.
        //
        return _shouldDeletePureCKliLogFile is true
                    || (_shouldDeletePureCKliLogFile is not false && arguments.HasHelp);
    }

    internal static void OnSuccessfulCKliLogCommand() => _shouldDeletePureCKliLogFile ??= true;

    internal static void OnAnyOtherCommandAndSuccess() => _shouldDeletePureCKliLogFile = false;

    internal static void OnInteractiveCommandExecuted( IActivityMonitor monitor, CommandLineArguments arguments )
    {
        var defaultOutput = GrandOutput.Default;
        if( defaultOutput != null )
        {
            // We fire & forget here because we can (and avoid an async context): this uses the capability
            // of the Text sink handler to forget the current file: the file won't appear at all on the
            // file system. If a "ckli log" is emitted, the previous one will be found.
            bool suppressFile = ShouldDeleteCurrentLogFile( arguments );
            _shouldDeletePureCKliLogFile = null;
            defaultOutput.Sink.Submit( new TextLogFileCloser( suppressFile, deactivateHandler: false ) );
        }
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if <see cref="Initialize"/> has not been called.
    /// </summary>
    [MemberNotNull( nameof( _screen ) )]
    public static void CheckInitialized() => Throw.CheckState( "CKliRootEnv.Initialize() must have been called before.", _screen != null );

    /// <summary>
    /// Gets instance name ("CKli" or "CKli-Test" for instance).
    /// </summary>
    public static string InstanceName
    {
        get
        {
            CheckInitialized();
            return _appLocalDataPath.LastPart;
        }
    }

    /// <summary>
    /// Gets the full path of the folder in <see cref="Environment.SpecialFolder.LocalApplicationData"/> to use.
    /// </summary>
    public static NormalizedPath AppLocalDataPath
    {
        get
        {
            CheckInitialized();
            return _appLocalDataPath;
        }
    }

    /// <summary>
    /// Gets the screen to use.
    /// </summary>
    public static IScreen Screen
    {
        get
        {
            CheckInitialized();
            return _screen!;
        }
    }

    /// <summary>
    /// Gets the secrets store to use.
    /// </summary>
    public static ISecretsStore SecretsStore
    {
        get
        {
            CheckInitialized();
            return _secretsStore!;
        }
    }

    /// <summary>
    /// Gets the initial current directory.
    /// </summary>
    public static NormalizedPath CurrentDirectory
    {
        get
        {
            CheckInitialized();
            return _currentDirectory;
        }
    }

    /// <summary>
    /// Gets the current <see cref="StackRepository.StackWorkingFolder"/> if initial <see cref="CurrentDirectory"/> is in a Stack directory.
    /// <see cref="NormalizedPath.IsEmptyPath"/> otherwise.
    /// </summary>
    public static NormalizedPath CurrentStackPath
    {
        get
        {
            CheckInitialized();
            return _currentStackPath;
        }
    }

    /// <summary>
    /// Gets the default CKli environment.
    /// </summary>
    public static CKliEnv DefaultCKliEnv
    {
        get
        {
            CheckInitialized();
            return _defaultCommandContext!;
        }
    }

    /// <summary>
    /// Gets or sets an optional provider for the "Global options:" help display.
    /// This is an awful global but we don't care.
    /// </summary>
    public static Func<ImmutableArray<(ImmutableArray<string> Names, string Description, bool Multiple)>>? GlobalOptions { get; set; }

    /// <summary>
    /// Gets or sets an optional provider for the "Global flags:" help display.
    /// This is an awful global but we don't care.
    /// </summary>
    public static Func<ImmutableArray<(ImmutableArray<string> Names, string Description)>>? GlobalFlags { get; set; }

    /// <summary>
    /// Acquires an exclusive global system lock for this environment: the key is the <see cref="AppLocalDataPath"/>.
    /// </summary>
    /// <param name="monitor">
    /// Monitor to use if available to warn if waiting is required.
    /// When null, a <see cref="Console.WriteLine(string?)"/> is used.
    /// </param>
    /// <returns>A mutex to be disposed once done.</returns>
    public static Mutex AcquireAppMutex( IActivityMonitor? monitor )
    {
        CheckInitialized();
        var mutex = new Mutex( true, _appLocalDataPath, out var acquired );
        if( !acquired )
        {
            var msg = $"Waiting for the '{_appLocalDataPath}' mutex to be released.";
            if( monitor != null )
            {
                monitor.UnfilteredLog( LogLevel.Warn | LogLevel.IsFiltered, null, msg, null );
            }
            else
            {
                Console.WriteLine( msg );
            }
            mutex.WaitOne();
        }
        return mutex;
    }

    /// <summary>
    /// Writes the current configuration.
    /// </summary>
    /// <param name="monitor">See <see cref="AcquireAppMutex(IActivityMonitor?)"/>.</param>
    public static void WriteConfiguration( IActivityMonitor? monitor )
    {
        CheckInitialized();
        Throw.DebugAssert( _secretsStore != null );
        using( AcquireAppMutex( monitor ) )
        {
            File.WriteAllText( GetConfigPath(), $"""
                {_secretsStore.GetType().GetWeakAssemblyQualifiedName()}

                """ );
        }
    }

    static NormalizedPath GetConfigPath() => _appLocalDataPath.AppendPart( "config.v0.txt" );

}
