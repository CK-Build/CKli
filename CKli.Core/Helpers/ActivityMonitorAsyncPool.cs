using CK.Core;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    }

    /// <summary>
    /// Gets a <see cref="IDisposableActivityMonitor"/> that must be disposed once done or null if the <paramref name="cancellationToken"/>
    /// has been signaled.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A disposable monitor or null if <paramref name="cancellationToken"/> has been signaled.</returns>
    public async ValueTask<IDisposableActivityMonitor?> GetAsync( CancellationToken cancellationToken = default )
    {
        if( cancellationToken.IsCancellationRequested )
        {
            return null;
        }
        try
        {
            await _semaphore.WaitAsync( cancellationToken ).ConfigureAwait( false );
        }
        catch( OperationCanceledException ex ) when (ex.CancellationToken == cancellationToken )
        {
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
            _pool = pool;
            Monitor = new ActivityMonitor();
        }

        internal IDisposableActivityMonitor Reuse()
        {
            _pool._firstFree = _nextFree;
            return new SingleUse( this );
        }

        internal void Free()
        {
            lock( _pool._lock )
            {
                _nextFree = _pool._firstFree;
                _pool._firstFree = this;
            }
            _pool._semaphore.Release();
        }
    }
}
