using CK.Core;
using CK.Monitoring;

namespace CKli.Core;

/// <summary>
/// The root environment is in charge of the <see cref="CurrentDirectory"/>, <see cref="CurrentStackPath"/>,
/// and to setup the monitoring.
/// <para>
/// This is not used by tests.
/// </para>
/// </summary>
public static class CKliRunEnv
{
    static NormalizedPath _currentDirectory;
    static NormalizedPath _currentStackPath;

    /// <summary>
    /// Initialize the CKli environment. 
    /// </summary>
    /// <param name="currentDirectory">Current directory to consider.</param>
    public static void Initialize( NormalizedPath currentDirectory )
    {
        CKliRootEnv.CheckInitialized();
        Throw.CheckState( "Initialize can be called only once.", CurrentDirectory.IsEmptyPath );
        _currentDirectory = currentDirectory;
        // To handle logs, we firts must determine if we are in a Stack. If this is the case, then the Logs/ folder
        // will be .XXXXStack/Logs, else the log will be in _appLocalDataPath/Out-of-Stack-Logs/.
        _currentStackPath = StackRepository.FindGitStackPath( currentDirectory );

        // When testing, the GrandOuput.Default is configured by tests and this is fine: we keep
        // the logs in the test Logs/ folder but tests should not use this anyway.
        // When not testing and the host application already configured the logging, we do nothing.
        if( GrandOutput.Default == null )
        {
            if( LogFile.RootLogPath == null )
            {
                LogFile.RootLogPath = _currentStackPath.IsEmptyPath
                                        ? CKliRootEnv.AppLocalDataPath.AppendPart( "Out-of-Stack-Logs" )
                                        : _currentStackPath.AppendPart( "Logs" );
            }
            ActivityMonitor.DefaultFilter = LogFilter.Diagnostic;
            GrandOutput.EnsureActiveDefault( new GrandOutputConfiguration()
            {
                Handlers = { new CK.Monitoring.Handlers.TextFileConfiguration { MaximumTotalKbToKeep = 2 * 1024 /*2 MBytes */ } }
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

    /// <summary>
    /// Gets the current directory.
    /// </summary>
    public static NormalizedPath CurrentDirectory => _currentDirectory;

    /// <summary>
    /// Gets the current <see cref="StackRepository.StackWorkingFolder"/> if <see cref="CurrentDirectory"/> is in a Stack directory
    /// or the <see cref="NormalizedPath.IsEmptyPath"/>.
    /// </summary>
    public static NormalizedPath CurrentStackPath => _currentStackPath;
}
