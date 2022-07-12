using CK.Core;
using NuGet.Common;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace CK.Env.NuGet
{
    /// <summary>
    /// It is hard to say whether the NuGet logger is used concurrency or not.
    /// In doubt, we serialize with a basic lock.
    /// </summary>
    sealed class NuGetLoggerAdapter : ILogger
    {
        readonly object _lock;

        public NuGetLoggerAdapter( IActivityMonitor monitor )
        {
            Monitor = monitor;
            _lock = new object();
        }

        /// <summary>
        /// Dangerous reference to the inner monitor.
        /// Use <see cref="SafeLog"/> method.
        /// </summary>
        public readonly IActivityMonitor Monitor;

        /// <summary>
        /// Takes the lock and calls the action.
        /// </summary>
        /// <param name="log">The log action.</param>
        public void SafeLog( Action<IActivityMonitor> log )
        {
            lock( _lock )
            {
                log( Monitor );
            }
        }

        public void LogDebug( string data ) { lock( _lock ) LogWithRetries( () => Monitor.Debug( $"NuGet: {data}" ) ); }
        public void LogVerbose( string data ) { lock( _lock ) LogWithRetries( () => Monitor.Info( $"NuGet: {data}" ) ); }
        public void LogInformation( string data ) { lock( _lock ) LogWithRetries( () => Monitor.Info( $"NuGet: {data}" ) ); }
        public void LogMinimal( string data ) { lock( _lock ) LogWithRetries( () => Monitor.Info( $"NuGet: {data}" ) ); }
        public void LogWarning( string data ) { lock( _lock ) LogWithRetries( () => Monitor.Warn( $"NuGet: {data}" ) ); }
        public void LogError( string data ) { lock( _lock ) LogWithRetries( () => Monitor.Error( $"NuGet: {data}" ) ); }
        public void LogSummary( string data ) { lock( _lock ) LogWithRetries( () => Monitor.Info( $"NuGet: {data}" ) ); }
        public void LogInformationSummary( string data ) { lock( _lock ) LogWithRetries( () => Monitor.Info( $"NuGet: {data}" ) ); }
        public void Log( global::NuGet.Common.LogLevel level, string data ) { lock( _lock ) LogWithRetries( () => Monitor.Info( $"NuGet ({level}): {data}" ) ); }
        public Task LogAsync( global::NuGet.Common.LogLevel level, string data )
        {
            Log( level, data );
            return Task.CompletedTask;
        }

        public void Log( ILogMessage message )
        {
            lock( _lock ) Monitor.Info( $"NuGet ({message.Level}) - Code: {message.Code} - Project: {message.ProjectPath} - {message.Message}" );
        }

        public Task LogAsync( ILogMessage message )
        {
            LogWithRetries( () => Log( message ) );
            return Task.CompletedTask;
        }

        [SuppressMessage( "Design", "CA1031:Do not catch general exception types", Justification = "Ugly hack. Cannot log anyway." )]
        void LogWithRetries( Action action )
        {
            for( int i = 0; i < 4; i++ )
            {
                try
                {
                    action();
                    return;
                }
                catch
                {
                }
            }
            action();
        }
    }

}
