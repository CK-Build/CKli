using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Core;

/// <summary>
/// Concurrent pool of reusable <see cref="IDisposableActivityMonitor"/>.
/// <para>
/// Obtained instances must be disposed (any number of times) and, once disposed, are guaranteed
/// to throw <see cref="ObjectDisposedException"/> on any reuse attempt: no monitor from the pool
/// can be inadvertently reused.
/// </para>
/// <para>
/// This pool is used as a resource limiter for parallelization of independent tasks (typically <see cref="Repo"/>
/// based tasks).
/// </para>
/// </summary>
public sealed class ActivityMonitorAsyncPool
{
    readonly SemaphoreSlim _semaphore;
    readonly Lock _lock;
    readonly int _maxCount;
    Reusable? _firstFree;

    /// <summary>
    /// Initializes a new pool with at most <paramref name="maxCount"/> monitors.
    /// </summary>
    /// <param name="maxCount">The maximal number of concurrently alive monitors.</param>
    public ActivityMonitorAsyncPool( int maxCount )
    {
        Throw.CheckArgument( maxCount > 0 );
        _semaphore = new SemaphoreSlim( maxCount, maxCount );
        _lock = new Lock();
        _maxCount = maxCount;
    }

    /// <summary>
    /// Gets the maximal number of concurrently alive monitors.
    /// </summary>
    public int MaxCount => _maxCount;

    /// <summary>
    /// Gets a <see cref="IDisposableActivityMonitor"/> that must be disposed once done or null if the <paramref name="cancellation"/>
    /// has been signaled.
    /// </summary>
    /// <param name="cancellation">Optional cancellation token.</param>
    /// <returns>A disposable monitor or null if <paramref name="cancellation"/> has been signaled.</returns>
    public async ValueTask<IDisposableActivityMonitor?> GetAsync( CancellationToken cancellation = default )
    {
        if( cancellation.IsCancellationRequested )
        {
            //ActivityMonitor.StaticLogger.Trace( $"Cancelled-0" );
            return null;
        }
        try
        {
            await _semaphore.WaitAsync( cancellation ).ConfigureAwait( false );
        }
        catch( OperationCanceledException ex ) when (ex.CancellationToken == cancellation )
        {
            //ActivityMonitor.StaticLogger.Trace( $"Cancelled-1" );
            return null;
        }

        lock( _lock )
        {
            Reusable? o;
            if( (o = _firstFree) != null )
            {
                return o.Reuse();
            }
            return new SingleUse( new Reusable( this ) );
        }
    }

    /// <summary>
    /// Parallel helper on synchronous action that can be interrupted by a cancellation token.
    /// <para>
    /// When 
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type of the item to process in parallel.</typeparam>
    /// <param name="objects">The items to process.</param>
    /// <param name="safeAction">
    /// The action to apply to each item.
    /// This action must not throw exceptions: failure must result in a gentle false returned value (including
    /// signaled <paramref name="cancellation"/>).
    /// </param>
    /// <param name="onError">Configures behavior when an error occurs regarding the other parallel tasks.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public async Task<bool> ParallelAsync<T>( IEnumerable<T> objects,
                                              Func<IActivityMonitor, T, CancellationToken, bool> safeAction,
                                              ParallelErrorBehavior onError,
                                              CancellationToken cancellation )
    {
        if( onError == ParallelErrorBehavior.Ignore )
        {
            var tasks = objects.Select( o => Task.Run( async () =>
            {
                using var monitor = await GetAsync( cancellation ).ConfigureAwait( false );
                if( monitor == null ) return false;
                try
                {
                    return safeAction( monitor, o, cancellation );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"Unhandled error in Parallel task.", ex );
                    return false;
                }
            } ) ).ToArray();
            var results = await Task.WhenAll( tasks ).ConfigureAwait( false );
            return results.All( Util.FuncIdentity );
        }
        else
        {
            var cts = new CancellationTokenSource();
            using var reg = cancellation.UnsafeRegister( static o => ((CancellationTokenSource)o!).Cancel(), cts );
            var tasks = objects.Select( o => Task.Run( async () =>
            {
                using var monitor = await GetAsync( cts.Token ).ConfigureAwait( false );
                if( monitor == null ) return false;
                try
                {
                    if( safeAction( monitor, o, onError == ParallelErrorBehavior.HardStop ? cts.Token : cancellation ) )
                    {
                        return true;
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( $"Unhandled error in Parallel task.", ex );
                }
                cts.Cancel();
                return false;
            } ) ).ToArray();
            var results = await Task.WhenAll( tasks ).ConfigureAwait( false );
            return results.All( Util.FuncIdentity );
        }
    }

    /// <summary>
    /// This implements the IDisposableActivityMonitor in an independent, non shared, object: it can be disposed once
    /// and only once and cannot be reused inadvertently.
    /// </summary>
    sealed class SingleUse : IDisposableActivityMonitor
    {
        Reusable? _reusable;

        public SingleUse( Reusable reusable )
        {
            _reusable = reusable;
        }

        public void Dispose()
        {
            var p = Interlocked.Exchange( ref _reusable, null );
            p?.Free();
        }

        ActivityMonitor GetMonitor()
        {
            var r = _reusable;
            return r != null ? r.Monitor : Throw.ObjectDisposedException<ActivityMonitor>( "Pooled monitor has been disposed." );
        }

        #region IActivityMonitor
        public string UniqueId => GetMonitor().UniqueId;

        [AllowNull]
        public CKTrait AutoTags { get => GetMonitor().AutoTags; set => GetMonitor().AutoTags = value; }

        public LogFilter MinimalFilter { get => GetMonitor().MinimalFilter; set => GetMonitor().MinimalFilter = value; }

        public LogFilter ActualFilter => GetMonitor().ActualFilter;

        public string Topic => GetMonitor().Topic;

        public IActivityMonitorOutput Output => GetMonitor().Output;

        public IParallelLogger ParallelLogger => GetMonitor().ParallelLogger;

        LogLevelFilter IActivityLineEmitter.ActualFilter => ((IActivityLineEmitter)GetMonitor()).ActualFilter;

        public void SetTopic( string? newTopic, [CallerFilePath] string? fileName = null, [CallerLineNumber] int lineNumber = 0 ) => GetMonitor().SetTopic( newTopic, fileName, lineNumber );

        public IDisposableGroup UnfilteredOpenGroup( ref ActivityMonitorLogData data ) => GetMonitor().UnfilteredOpenGroup( ref data );

        public bool CloseGroup( object? userConclusion = null ) => GetMonitor().CloseGroup( userConclusion );

        ActivityMonitorLogData IActivityLineEmitter.CreateActivityMonitorLogData( LogLevel level, CKTrait finalTags, string? text, object? exception, string? fileName, int lineNumber, bool isOpenGroup )
        {
            return ((IActivityLineEmitter)GetMonitor()).CreateActivityMonitorLogData( level, finalTags, text, exception, fileName, lineNumber, isOpenGroup );
        }

        public void UnfilteredLog( ref ActivityMonitorLogData data ) => GetMonitor().UnfilteredLog( ref data );

        public ActivityMonitor.Token CreateToken( string? message = null, string? dependentTopic = null, CKTrait? createTags = null, [CallerFilePath] string? fileName = null, [CallerLineNumber] int lineNumber = 0 )
        {
            return GetMonitor().CreateToken( message, dependentTopic, createTags, fileName, lineNumber );
        } 
        #endregion
    }

    /// <summary>
    /// This implements the free list of ActivityMonitor.
    /// It is not directly exposed to avoid https://en.wikipedia.org/wiki/ABA_problem. The SingleUse shell guaranties
    /// the single use semantics.
    /// </summary>
    sealed class Reusable
    {
        public readonly ActivityMonitor Monitor;
        readonly ActivityMonitorAsyncPool _pool;
        internal Reusable? _nextFree;

        internal Reusable( ActivityMonitorAsyncPool pool )
        {
            Monitor = new ActivityMonitor();
            _pool = pool;
            //ActivityMonitor.StaticLogger.Trace( $"Create-{Monitor.UniqueId}" );
        }

        internal IDisposableActivityMonitor Reuse()
        {
            //ActivityMonitor.StaticLogger.Trace( $"Reuse-{Monitor.UniqueId}" );
            Throw.DebugAssert( _pool._lock.IsHeldByCurrentThread );
            _pool._firstFree = _nextFree;
            return new SingleUse( this );
        }

        internal void Free()
        {
            //ActivityMonitor.StaticLogger.Trace( $"Free-{Monitor.UniqueId}" );
            lock( _pool._lock )
            {
                _nextFree = _pool._firstFree;
                _pool._firstFree = this;
            }
            _pool._semaphore.Release();
        }

        public override string ToString() => Monitor.UniqueId;
    }
}
