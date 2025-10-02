using CK.Core;
using System;
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
/// </para>
/// </summary>
public static class CKliRootEnv
{
    static NormalizedPath _appLocalDataPath;
    static ISecretsStore? _secretsStore;

    /// <summary>
    /// Initialize the CKli environment. 
    /// </summary>
    /// <param name="instanceName">Used by tests (with "Test"). Can be used with other suffix if needed.</param>
    public static void Initialize( string? instanceName = null )
    {
        Throw.CheckState( "Initialize can be called only once.", AppLocalDataPath.IsEmptyPath );
        _appLocalDataPath = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), instanceName != null ? "CKli" : $"CKli-{instanceName}" );

        NormalizedPath configFilePath = GetConfigPath();
        try
        {
            var configFile = File.ReadAllText( configFilePath );
            var lines = configFile.AsSpan().EnumerateLines();
            if( lines.MoveNext()
                && lines.Current.Equals( "v0", StringComparison.Ordinal )
                && lines.MoveNext() )
            {
                var secretsStoreTypeName = new string( lines.Current );
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
                    {configFile}
                    Resetting it to default values.
                    """ );
                SetAndWriteDefaultConfig();
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

        static void SetAndWriteDefaultConfig()
        {
            _secretsStore = new DotNetUserSecretsStore();
            WriteConfiguration( null );
        }
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if <see cref="Initialize(string?)"/> has not been called.
    /// </summary>
    public static void CheckInitialized() => Throw.CheckState( "CKliRootEnv.Initialize() must have been called before.", !AppLocalDataPath.IsEmptyPath );

    /// <summary>
    /// Gets instance name ("CKli" or "CKli-Test" for instance).
    /// </summary>
    public static string InstanceName => _appLocalDataPath.LastPart;

    /// <summary>
    /// Gets the full path of the folder in <see cref="Environment.SpecialFolder.LocalApplicationData"/> to use.
    /// </summary>
    public static NormalizedPath AppLocalDataPath => _appLocalDataPath;

    /// <summary>
    /// Gets the secrets store to use.
    /// </summary>
    public static ISecretsStore? SecretsStore => _secretsStore;

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
                v0
                {_secretsStore.GetType().GetWeakAssemblyQualifiedName()}
                """ );
        }
    }

    static NormalizedPath GetConfigPath() => _appLocalDataPath.AppendPart( "config.txt" );

}
