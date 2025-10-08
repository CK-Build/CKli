using CK.Core;
using CK.Monitoring;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace CKli.Core;

/// <summary>
/// The root environment is in charge of the <see cref="AppLocalDataPath"/> and to provide a <see cref="ISecretsStore"/>.
/// It may be extended in the future to handle other basic locally configurable aspect but currently it has all what we need.
/// This must be initialized before anything can be done with the <see cref="StackRepository"/>.
/// <para>
/// This is a static class. Tests use the <see cref="Initialize(string?)"/> instance name to isolate the test environment ("CKli-Test")
/// from the regular run environment ("CKli").
/// This captures the initial <see cref="Environment.CurrentDirectory"/> and initializes the <see cref="GrandOutput.Default"/> if it is
/// not already initialized.
/// </para>
/// </summary>
public static class CKliRootEnv
{
    static NormalizedPath _appLocalDataPath;
    static ISecretsStore? _secretsStore;
    static NormalizedPath _currentDirectory;
    static NormalizedPath _currentStackPath;
    static CommandCommonContext? _defaultCommandContext;

    /// <summary>
    /// Initializes the CKli environment. This captures the <see cref="Environment.CurrentDirectory"/> and
    /// initializes the 
    /// </summary>
    /// <param name="instanceName">Used by tests (with "Test"). Can be used with other suffix if needed.</param>
    public static void Initialize( string? instanceName = null )
    {
        Throw.CheckState( "Initialize can be called only once.", _appLocalDataPath.IsEmptyPath );
        _appLocalDataPath = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), instanceName == null ? "CKli" : $"CKli-{instanceName}" );
        // To handle logs, we firts must determine if we are in a Stack. If this is the case, then the Logs/ folder
        // will be .[Public|PrivateStack]/Logs, else the log will be in _appLocalDataPath/Out-of-Stack-Logs/.
        _currentDirectory = Environment.CurrentDirectory;
        _currentStackPath = StackRepository.FindGitStackPath( _currentDirectory );
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
                    Console.WriteLine( $"""
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
            Console.WriteLine( $"""
                Error while initializing CKliRootEnv:
                {CKExceptionData.CreateFrom( ex ).ToString()}
                Resetting the '{configFilePath}' to default values.
                """ );
            SetAndWriteDefaultConfig();
        }

        _defaultCommandContext = new CommandCommonContext( _secretsStore, _currentDirectory, _currentStackPath );

        [MemberNotNull( nameof(_secretsStore) )]
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
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if <see cref="Initialize(string?)"/> has not been called.
    /// </summary>
    public static void CheckInitialized() => Throw.CheckState( "CKliRootEnv.Initialize() must have been called before.", !_appLocalDataPath.IsEmptyPath );

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
    /// Gets the default command context.
    /// </summary>
    public static CommandCommonContext DefaultCommandContext
    {
        get
        {
            CheckInitialized();
            return _defaultCommandContext!;
        }
    }

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
