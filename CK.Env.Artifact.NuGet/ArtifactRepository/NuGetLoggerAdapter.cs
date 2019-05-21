using CK.Core;
using NuGet.Common;
using System.Threading.Tasks;

namespace CK.Env.NuGet
{
    /// <summary>
    /// It is hard to say whether the NuGet logger is used concurrency or not.
    /// In doubt, we serialize with a basic lock.
    /// </summary>
    class NuGetLoggerAdapter : ILogger
    {
        readonly object _lock;

        public NuGetLoggerAdapter( IActivityMonitor monitor )
        {
            Monitor = monitor;
            _lock = new object();
        }

        public readonly IActivityMonitor Monitor;

        public void LogDebug( string data ) { lock( _lock ) Monitor.Debug( $"NuGet: {data}" ); }
        public void LogVerbose( string data ) { lock( _lock ) Monitor.Info( $"NuGet: {data}" ); }
        public void LogInformation( string data ) { lock( _lock ) Monitor.Info( $"NuGet: {data}" ); }
        public void LogMinimal( string data ) { lock( _lock ) Monitor.Info( $"NuGet: {data}" ); }
        public void LogWarning( string data ) { lock( _lock ) Monitor.Warn( $"NuGet: {data}" ); }
        public void LogError( string data ) { lock( _lock ) Monitor.Error( $"NuGet: {data}" ); }
        public void LogSummary( string data ) { lock( _lock ) Monitor.Info( $"NuGet: {data}" ); }
        public void LogInformationSummary( string data ) { lock( _lock ) Monitor.Info( $"NuGet: {data}" ); }
        public void Log( global::NuGet.Common.LogLevel level, string data ) { lock( _lock ) Monitor.Info( $"NuGet ({level}): {data}" ); }
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
            Log( message );
            return Task.CompletedTask;
        }
    }

}